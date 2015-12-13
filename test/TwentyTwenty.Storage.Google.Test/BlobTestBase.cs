﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Storage.v1;
using Google.Apis.Storage.v1.Data;
using TwentyTwenty.Storage;
using TwentyTwenty.Storage.Google;
using Xunit;
using Blob = Google.Apis.Storage.v1.Data.Object;

namespace TwentyTwenty.Storage.Google.Test
{
    [CollectionDefinition("BlobTestBase")]
    public class BaseCollection : ICollectionFixture<StorageFixture>
    {
    }

    [Collection("BlobTestBase")]
    public abstract class BlobTestBase : IClassFixture<StorageFixture>
    {
        protected static readonly Random _rand = new Random();
        protected StorageService _client;
        protected IStorageProvider _provider;
        protected IStorageProvider _exceptionProvider;
        protected string Bucket;
        protected string ContainerPrefix;

        /// <summary>
        /// {0} - Container name
        /// {1} - Blob name
        /// </summary>
        private const string ContainerBlobFormat = @"{0}/{1}";

        private const string DefaultContentType = "application/octet-stream";

        /// <summary>
        /// For blobs which have a "public" ACL.
        /// </summary>
        private readonly ObjectAccessControl PublicAcl = new ObjectAccessControl {Entity = "allUsers", Role = "READER"};

        public BlobTestBase(StorageFixture fixture)
        {
            Bucket = fixture.Config["GoogleBucket"];
            _client = fixture._client;
            _provider =
                new GoogleStorageProvider(new GoogleProviderOptions
                {
                    Email = fixture.Config["GoogleEmail"],
                    PrivateKey = fixture.Config["GooglePrivateKey"],
                    Bucket = Bucket
                });
            _exceptionProvider =
                new GoogleStorageProvider(new GoogleProviderOptions
                {
                    Email = null,
                    PrivateKey = null,
                    Bucket = Bucket
                });
        }

        public byte[] GenerateRandomBlob(int length = 256)
        {
            var buffer = new byte[length];
            _rand.NextBytes(buffer);
            return buffer;
        }

        public MemoryStream GenerateRandomBlobStream(int length = 256)
        {
            return new MemoryStream(GenerateRandomBlob(length));
        }

        public string GetRandomContainerName()
        {
            return StorageFixture.ContainerPrefix + GenerateRandomName();
        }

        public string GenerateRandomName()
        {
            return Guid.NewGuid().ToString("N");
        }

        protected async Task CreateNewObject(string container, string blobName, Stream data, bool isPublic = false,
            string contentType = null)
        {
            var blob = new Blob
            {
                Name = string.Format(ContainerBlobFormat, container, blobName),
                //TODO:  Figure out how the hell ACL has got to be tweaked to actually work.  Currently this does not do it, and the .NET api does not expose the ability to set the query parameter "predefinedAcl" which would be perfect for our needs here.
                ContentType = contentType ?? DefaultContentType
            };

            await _client.Objects.Insert(blob, Bucket, data, contentType ?? DefaultContentType).UploadAsync();

            if (isPublic)
            {
                await _client.ObjectAccessControls.Insert(PublicAcl, Bucket, blob.Name).ExecuteAsync();
            }
        }

        protected bool StreamEquals(Stream stream1, Stream stream2)
        {
            if (stream1.CanSeek)
            {
                stream1.Seek(0, SeekOrigin.Begin);
            }
            if (stream2.CanSeek)
            {
                stream2.Seek(0, SeekOrigin.Begin);
            }

            const int bufferSize = 2048;
            byte[] buffer1 = new byte[bufferSize]; //buffer size
            byte[] buffer2 = new byte[bufferSize];
            while (true)
            {
                int count1 = stream1.Read(buffer1, 0, bufferSize);
                int count2 = stream2.Read(buffer2, 0, bufferSize);

                if (count1 != count2)
                    return false;

                if (count1 == 0)
                    return true;

                // You might replace the following with an efficient "memcmp"
                if (!buffer1.Take(count1).SequenceEqual(buffer2.Take(count2)))
                    return false;
            }
        }

        //TODO:  Unfortunately, it does not appear to be easy to determine whether an error is due to not being authenticated; in this case a generic error message of "BaseURI cannot be null" - but this is not unique to authentication errors either.
        public void TestProviderAuth(Action<IStorageProvider> method)
        {
            var exception = Assert.Throws<StorageException>(() =>
            {
                method(_exceptionProvider);
            });
            Assert.Equal(exception.ErrorCode, (int)StorageErrorCode.GenericException);
        }

        public T TestProviderAuth<T>(Func<IStorageProvider, T> method)
        {
            var exception = Assert.Throws<StorageException>(() =>
            {
                method(_exceptionProvider);
            });
            Assert.Equal(exception.ErrorCode, (int)StorageErrorCode.GenericException);
            return method(_provider);
        }

        public async Task TestProviderAuthAsync(Func<IStorageProvider, Task> method)
        {
            var exception = await Assert.ThrowsAsync<StorageException>(() =>
            {
                return method(_exceptionProvider);
            });
            Assert.Equal(exception.ErrorCode, (int)StorageErrorCode.GenericException);
            await method(_provider);
        }
    }
}
