﻿// -----------------------------------------------------------------------------------------
// <copyright file="StorageExceptionTests.cs" company="Microsoft">
//    Copyright 2013 Microsoft Corporation
// 
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//      http://www.apache.org/licenses/LICENSE-2.0
// 
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
// </copyright>
// -----------------------------------------------------------------------------------------

namespace Microsoft.Azure.Storage
{
#if WINDOWS_DESKTOP && !WINDOWS_PHONE
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Microsoft.Azure.Storage.Blob;
    using Microsoft.Azure.Storage.Core.Util;
    using Microsoft.Azure.Storage.Shared.Protocol;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml;

    [TestClass]
    public class StorageExceptionTests
    {
        [TestMethod]
        [Description("Persist and read back StorageException")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StorageExceptionVerifyXml()
        {
            Uri baseAddressUri = new Uri(TestBase.TargetTenantConfig.BlobServiceEndpoint);
            CloudBlobClient client = new CloudBlobClient(baseAddressUri, TestBase.StorageCredentials);
            CloudBlobContainer container = client.GetContainerReference(Guid.NewGuid().ToString("N"));
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");

                byte[] buffer = new byte[1024];
                Random random = new Random();
                random.NextBytes(buffer);

                using(MemoryStream stream = new MemoryStream(buffer))
                {
                    blob.UploadFromStream(stream);
                }

                CloudPageBlob blob2 = container.GetPageBlobReference("blob1");
                StorageException e = TestHelper.ExpectedException<StorageException>(
                    () => blob2.FetchAttributes(),
                    "Fetching attributes of a block blob using a page blob reference should fail");

                using (Stream s = new MemoryStream())
                {
                    BinaryFormatter formatter = new BinaryFormatter();
                    formatter.Serialize(s, e);
                    s.Position = 0; // Reset stream position
                    StorageException e2 = (StorageException)formatter.Deserialize(s);

                    Assert.IsInstanceOfType(e2.InnerException, typeof(InvalidOperationException));
                    Assert.AreEqual(e.IsRetryable, e2.IsRetryable);
                    Assert.AreEqual(e.RequestInformation.HttpStatusCode, e2.RequestInformation.HttpStatusCode);
                    Assert.AreEqual(e.RequestInformation.HttpStatusMessage, e2.RequestInformation.HttpStatusMessage);
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Persist and read back ExtendedErrorInfo")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task ExtendedErrorInfoVerifyXml()
        {
            Uri baseAddressUri = new Uri(TestBase.TargetTenantConfig.BlobServiceEndpoint);
            CloudBlobClient client = new CloudBlobClient(baseAddressUri, TestBase.StorageCredentials);
            CloudBlobContainer container = client.GetContainerReference(Guid.NewGuid().ToString("N"));
            
            try
            {
                StorageException e = TestHelper.ExpectedException<StorageException>(
                    () => container.GetPermissions(),
                    "Try to get permissions on a non-existent container");

                Assert.IsNotNull(e.RequestInformation.ExtendedErrorInformation);

                StorageExtendedErrorInformation retrErrorInfo = new StorageExtendedErrorInformation();
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Indent = true;
                StringBuilder sb = new StringBuilder();
                using (XmlWriter writer = XmlWriter.Create(sb, settings))
                {
                    e.RequestInformation.ExtendedErrorInformation.WriteXml(writer);
                }

                using (XmlReader reader = XMLReaderExtensions.CreateAsAsync(new MemoryStream(Encoding.Unicode.GetBytes(sb.ToString()))))
                {
                    await retrErrorInfo.ReadXmlAsync(reader, CancellationToken.None);
                }

                Assert.AreEqual(e.RequestInformation.ExtendedErrorInformation.ErrorCode, retrErrorInfo.ErrorCode);
                Assert.AreEqual(e.RequestInformation.ExtendedErrorInformation.ErrorMessage, retrErrorInfo.ErrorMessage);
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Persist and read back ExtendedErrorInfo")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task ExtendedErrorInfoVerifyXmlWithAdditionalDetails()
        {
            Uri baseAddressUri = new Uri(TestBase.TargetTenantConfig.BlobServiceEndpoint);
            CloudBlobClient client = new CloudBlobClient(baseAddressUri, TestBase.StorageCredentials);
            CloudBlobContainer container = client.GetContainerReference(Guid.NewGuid().ToString("N"));

            byte[] buffer = TestBase.GetRandomBuffer(4 * 1024 * 1024);
            MD5 md5 = MD5.Create();
            string contentMD5 = Convert.ToBase64String(md5.ComputeHash(buffer));

            try
            {
                container.Create();
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                List<string> blocks = new List<string>();
                for (int i = 0; i < 2; i++)
                {
                    blocks.Add(Convert.ToBase64String(Guid.NewGuid().ToByteArray()));
                }

                using (MemoryStream memoryStream = new MemoryStream(buffer))
                {
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    blob.PutBlock(blocks[0], memoryStream, contentMD5);

                    int offset = buffer.Length - 1024;
                    memoryStream.Seek(offset, SeekOrigin.Begin);
                    StorageException e = TestHelper.ExpectedException<StorageException>(
                        () => blob.PutBlock(blocks[1], memoryStream, contentMD5),
                        "Invalid MD5 should fail with mismatch");

                    Assert.IsNotNull(e.RequestInformation.ExtendedErrorInformation);

                    StorageExtendedErrorInformation retrErrorInfo = new StorageExtendedErrorInformation();
                    XmlWriterSettings settings = new XmlWriterSettings();
                    settings.Indent = true;
                    StringBuilder sb = new StringBuilder();
                    using (XmlWriter writer = XmlWriter.Create(sb, settings))
                    {
                        e.RequestInformation.ExtendedErrorInformation.WriteXml(writer);
                    }

                    using (XmlReader reader = XMLReaderExtensions.CreateAsAsync(new MemoryStream(Encoding.Unicode.GetBytes(sb.ToString()))))
                    {
                        await retrErrorInfo.ReadXmlAsync(reader, CancellationToken.None);
                    }

                    Assert.AreEqual(e.RequestInformation.ExtendedErrorInformation.ErrorCode, retrErrorInfo.ErrorCode);
                    Assert.AreEqual(e.RequestInformation.ExtendedErrorInformation.ErrorMessage, retrErrorInfo.ErrorMessage);
                    Assert.AreNotEqual(0, retrErrorInfo.AdditionalDetails.Count);
                    Assert.AreEqual(e.RequestInformation.ExtendedErrorInformation.AdditionalDetails.Count, retrErrorInfo.AdditionalDetails.Count);
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Persist and read back RequestResult")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.Cloud)]
        public async Task RequestResultVerifyXml()
        {
            Uri baseAddressUri = new Uri(TestBase.TargetTenantConfig.BlobServiceEndpoint);
            CloudBlobClient blobClient = new CloudBlobClient(baseAddressUri, TestBase.StorageCredentials);
            CloudBlobContainer container = blobClient.GetContainerReference(Guid.NewGuid().ToString("N"));

            OperationContext opContext = new OperationContext();
            Assert.IsNull(opContext.LastResult);
            container.Exists(null, opContext);
            Assert.IsNotNull(opContext.LastResult);

            // We do not have precision at milliseconds level. Hence, we need
            // to recreate the start DateTime to be able to compare it later.
            DateTime start = opContext.LastResult.StartTime;
            start = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, start.Second);

            DateTime end = opContext.LastResult.EndTime;
            end = new DateTime(end.Year, end.Month, end.Day, end.Hour, end.Minute, end.Second);

            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            StringBuilder sb = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(sb, settings))
            {
                opContext.LastResult.WriteXml(writer);
            }

            RequestResult retrResult = new RequestResult();
            using (XmlReader reader = XMLReaderExtensions.CreateAsAsync(new MemoryStream(Encoding.Unicode.GetBytes(sb.ToString()))))
            {
                await retrResult.ReadXmlAsync(reader);
            }

            Assert.AreEqual(opContext.LastResult.RequestDate, retrResult.RequestDate);
            Assert.AreEqual(opContext.LastResult.ServiceRequestID, retrResult.ServiceRequestID);
            Assert.AreEqual(start, retrResult.StartTime);
            Assert.AreEqual(end, retrResult.EndTime);
            Assert.AreEqual(opContext.LastResult.HttpStatusCode, retrResult.HttpStatusCode);
            Assert.AreEqual(opContext.LastResult.HttpStatusMessage, retrResult.HttpStatusMessage);
            Assert.AreEqual(opContext.LastResult.ContentMd5, retrResult.ContentMd5);
            Assert.AreEqual(opContext.LastResult.ContentCrc64, retrResult.ContentCrc64);
            Assert.AreEqual(opContext.LastResult.Etag, retrResult.Etag);

            // Now test with no indentation
            sb = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(sb))
            {
                opContext.LastResult.WriteXml(writer);
            }

            retrResult = new RequestResult();
            using (XmlReader reader = XMLReaderExtensions.CreateAsAsync(new MemoryStream(Encoding.Unicode.GetBytes(sb.ToString()))))
            {
                await retrResult.ReadXmlAsync(reader);
            }

            Assert.AreEqual(opContext.LastResult.RequestDate, retrResult.RequestDate);
            Assert.AreEqual(opContext.LastResult.ServiceRequestID, retrResult.ServiceRequestID);
            Assert.AreEqual(start, retrResult.StartTime);
            Assert.AreEqual(end, retrResult.EndTime);
            Assert.AreEqual(opContext.LastResult.HttpStatusCode, retrResult.HttpStatusCode);
            Assert.AreEqual(opContext.LastResult.HttpStatusMessage, retrResult.HttpStatusMessage);
            Assert.AreEqual(opContext.LastResult.ContentMd5, retrResult.ContentMd5);
            Assert.AreEqual(opContext.LastResult.ContentCrc64, retrResult.ContentCrc64);
            Assert.AreEqual(opContext.LastResult.Etag, retrResult.Etag);
        }

        [TestMethod]
        [Description("Verify RequestResult ErrorCode property set")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.Cloud)]
        public async Task RequestResultErrorCode()
        {
            Uri baseAddressUri = new Uri(TestBase.TargetTenantConfig.BlobServiceEndpoint);
            CloudBlobClient client = new CloudBlobClient(baseAddressUri, TestBase.StorageCredentials);
            CloudBlobContainer container = client.GetContainerReference(Guid.NewGuid().ToString("N"));

            byte[] buffer = TestBase.GetRandomBuffer(4 * 1024 * 1024);
            MD5 md5 = MD5.Create();
            string contentMD5 = Convert.ToBase64String(md5.ComputeHash(buffer));

            try
            {
                RequestResult requestResult;
                XmlWriterSettings settings;
                StringBuilder sb;
                container.Create();
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                List<string> blocks = new List<string>();
                for (int i = 0; i < 2; i++)
                {
                    blocks.Add(Convert.ToBase64String(Guid.NewGuid().ToByteArray()));
                }

                // Verify the ErrorCode property is set and that it is serialized correctly
                using (MemoryStream memoryStream = new MemoryStream(buffer))
                {
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    blob.PutBlock(blocks[0], memoryStream, contentMD5);

                    int offset = buffer.Length - 1024;
                    memoryStream.Seek(offset, SeekOrigin.Begin);
                    StorageException e = TestHelper.ExpectedException<StorageException>(
                        () => blob.PutBlock(blocks[1], memoryStream, contentMD5),
                        "Invalid MD5 should fail with mismatch");

                    Assert.AreEqual(e.RequestInformation.ErrorCode, StorageErrorCodeStrings.Md5Mismatch);

                    requestResult = new RequestResult();
                    settings = new XmlWriterSettings();
                    settings.Indent = true;
                    sb = new StringBuilder();
                    using (XmlWriter writer = XmlWriter.Create(sb, settings))
                    {
                        e.RequestInformation.WriteXml(writer);
                    }

                    using (XmlReader reader = XMLReaderExtensions.CreateAsAsync(new MemoryStream(Encoding.Unicode.GetBytes(sb.ToString()))))
                    {
                        await requestResult.ReadXmlAsync(reader);
                    }

                    // ExtendedErrorInformation.ErrorCode will be depricated, but it should still match on a non HEAD request
                    Assert.AreEqual(e.RequestInformation.ErrorCode, requestResult.ErrorCode);
                    Assert.AreEqual(e.RequestInformation.ExtendedErrorInformation.ErrorCode, requestResult.ErrorCode);
                }

                // Verify the ErrorCode property is set on a HEAD request
                CloudAppendBlob blob2 = container.GetAppendBlobReference("blob2");
                blob2.CreateOrReplace();
                StorageException e2 = TestHelper.ExpectedException<StorageException>(
                    () => blob2.FetchAttributes(AccessCondition.GenerateIfMatchCondition("\"garbage\"")), // must supply our own quotes for a valid etag
                    "Mismatched etag should fail");
                    Assert.AreEqual(e2.RequestInformation.ErrorCode, StorageErrorCodeStrings.ConditionNotMet);

                // Verify the ErrorCode property is not set on a successful request and that it is serialized correctly
                OperationContext ctx = new OperationContext();
                blob2.FetchAttributes(operationContext: ctx);
                Assert.AreEqual(ctx.RequestResults[0].ErrorCode, null);
                requestResult = new RequestResult();
                settings = new XmlWriterSettings();
                settings.Indent = true;
                sb = new StringBuilder();
                using (XmlWriter writer = XmlWriter.Create(sb, settings))
                {
                    ctx.RequestResults[0].WriteXml(writer);
                }

                using (XmlReader reader = XMLReaderExtensions.CreateAsAsync(new MemoryStream(Encoding.Unicode.GetBytes(sb.ToString()))))
                {
                    await requestResult.ReadXmlAsync(reader);
                }

                Assert.AreEqual(ctx.RequestResults[0].ErrorCode, requestResult.ErrorCode);
            }
            finally
            {
                container.DeleteIfExists();
            }
        }
    }
#endif
}
