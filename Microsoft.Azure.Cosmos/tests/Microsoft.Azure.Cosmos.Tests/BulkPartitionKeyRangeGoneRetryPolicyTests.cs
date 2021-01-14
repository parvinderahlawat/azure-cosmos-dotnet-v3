﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Routing;
    using Microsoft.Azure.Documents;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;

    [TestClass]
    public class BulkPartitionKeyRangeGoneRetryPolicyTests
    {
        [TestMethod]
        public async Task NotRetryOnSuccess()
        {
            IDocumentClientRetryPolicy retryPolicy = new BulkPartitionKeyRangeGoneRetryPolicy(
                Mock.Of<ContainerInternal>(),
                new ResourceThrottleRetryPolicy(1));

            TransactionalBatchOperationResult result = new TransactionalBatchOperationResult(HttpStatusCode.OK);
            ShouldRetryResult shouldRetryResult = await retryPolicy.ShouldRetryAsync(result.ToResponseMessage(), default);
            Assert.IsFalse(shouldRetryResult.ShouldRetry);            
        }

        [TestMethod]
        public async Task RetriesOn429()
        {
            IDocumentClientRetryPolicy retryPolicy = new BulkPartitionKeyRangeGoneRetryPolicy(
                Mock.Of<ContainerInternal>(),
                new ResourceThrottleRetryPolicy(1));

            TransactionalBatchOperationResult result = new TransactionalBatchOperationResult((HttpStatusCode)StatusCodes.TooManyRequests);
            ShouldRetryResult shouldRetryResult = await retryPolicy.ShouldRetryAsync(result.ToResponseMessage(), default);
            Assert.IsTrue(shouldRetryResult.ShouldRetry);
        }

        [TestMethod]
        public async Task RetriesOn413()
        {
            IDocumentClientRetryPolicy retryPolicy = new BulkPartitionKeyRangeGoneRetryPolicy(
                Mock.Of<ContainerInternal>(),
                new ResourceThrottleRetryPolicy(1));

            TransactionalBatchOperationResult result = new TransactionalBatchOperationResult(HttpStatusCode.RequestEntityTooLarge);
            ShouldRetryResult shouldRetryResult = await retryPolicy.ShouldRetryAsync(result.ToResponseMessage(), default);
            Assert.IsTrue(shouldRetryResult.ShouldRetry);
        }

        [TestMethod]
        public async Task RetriesOnSplits()
        {
            IDocumentClientRetryPolicy retryPolicy = new BulkPartitionKeyRangeGoneRetryPolicy(
                GetSplitEnabledContainer(),
                new ResourceThrottleRetryPolicy(1));

            TransactionalBatchOperationResult result = new TransactionalBatchOperationResult(HttpStatusCode.Gone) { SubStatusCode = SubStatusCodes.PartitionKeyRangeGone };
            ShouldRetryResult shouldRetryResult = await retryPolicy.ShouldRetryAsync(result.ToResponseMessage(), default);
            Assert.IsTrue(shouldRetryResult.ShouldRetry);
        }

        [TestMethod]
        public async Task RetriesOnCompletingSplits()
        {
            IDocumentClientRetryPolicy retryPolicy = new BulkPartitionKeyRangeGoneRetryPolicy(
                GetSplitEnabledContainer(),
                new ResourceThrottleRetryPolicy(1));

            TransactionalBatchOperationResult result = new TransactionalBatchOperationResult(HttpStatusCode.Gone) { SubStatusCode = SubStatusCodes.CompletingSplit };
            ShouldRetryResult shouldRetryResult = await retryPolicy.ShouldRetryAsync(result.ToResponseMessage(), default);
            Assert.IsTrue(shouldRetryResult.ShouldRetry);
        }

        [TestMethod]
        public async Task RetriesOnCompletingPartitionMigrationSplits()
        {
            IDocumentClientRetryPolicy retryPolicy = new BulkPartitionKeyRangeGoneRetryPolicy(
                GetSplitEnabledContainer(),
                new ResourceThrottleRetryPolicy(1));

            TransactionalBatchOperationResult result = new TransactionalBatchOperationResult(HttpStatusCode.Gone) { SubStatusCode = SubStatusCodes.CompletingPartitionMigration };
            ShouldRetryResult shouldRetryResult = await retryPolicy.ShouldRetryAsync(result.ToResponseMessage(), default);
            Assert.IsTrue(shouldRetryResult.ShouldRetry);
        }

        private static ContainerInternal GetSplitEnabledContainer()
        {
            Mock<ContainerInternal> container = new Mock<ContainerInternal>();
            container.Setup(c => c.GetCachedRIDAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid().ToString());
            Mock<CosmosClientContext> context = new Mock<CosmosClientContext>();
            container.Setup(c => c.ClientContext).Returns(context.Object);
            context.Setup(c => c.DocumentClient).Returns(new ClientWithSplitDetection());
            return container.Object;
        }

        private class ClientWithSplitDetection : MockDocumentClient
        {
            private readonly Mock<PartitionKeyRangeCache> partitionKeyRangeCache;

            public ClientWithSplitDetection()
            {
                this.partitionKeyRangeCache = new Mock<PartitionKeyRangeCache>(MockBehavior.Strict, null, null, null);
                this.partitionKeyRangeCache.Setup(
                        m => m.TryGetOverlappingRangesAsync(
                            It.IsAny<string>(),
                            It.IsAny<Documents.Routing.Range<string>>(),
                            It.Is<bool>(b => b == true) // Mocking only the refresh, if it doesn't get called, the test fails
                        )
                ).Returns((string collectionRid, Documents.Routing.Range<string> range, bool forceRefresh) => Task.FromResult<IReadOnlyList<PartitionKeyRange>>(this.ResolveOverlapingPartitionKeyRanges(collectionRid, range, forceRefresh)));
            }

            internal override Task<PartitionKeyRangeCache> GetPartitionKeyRangeCacheAsync()
            {
                return Task.FromResult(this.partitionKeyRangeCache.Object);
            }

        }
    }
}
