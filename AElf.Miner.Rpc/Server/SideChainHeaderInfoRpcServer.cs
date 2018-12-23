﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AElf.ChainController;
using AElf.Common.Attributes;
using AElf.Kernel;
using Grpc.Core;
using AElf.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AElf.Miner.Rpc.Server
{
    [LoggerName("SideChainRpcServer")]
    public class SideChainBlockInfoRpcServer : SideChainBlockInfoRpc.SideChainBlockInfoRpcBase
    {
        private readonly IChainService _chainService;
        public ILogger<SideChainBlockInfoRpcServer> Logger {get;set;}
        private ILightChain LightChain { get; set; }

        public SideChainBlockInfoRpcServer(IChainService chainService)
        {
            _chainService = chainService;
            Logger = NullLogger<SideChainBlockInfoRpcServer>.Instance;
        }

        public void Init(Hash chainId)
        {
            LightChain = _chainService.GetLightChain(chainId);
        }

        /// <summary>
        /// Response to indexing request from main chain node.
        /// Many requests to many responses.
        /// </summary>
        /// <param name="requestStream"></param>
        /// <param name="responseStream"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override async Task IndexDuplexStreaming(IAsyncStreamReader<RequestBlockInfo> requestStream, 
            IServerStreamWriter<ResponseSideChainBlockInfo> responseStream, ServerCallContext context)
        {
            // TODO: verify the from address and the chain 
            Logger.LogDebug("Side Chain Server received IndexedInfo message.");

            try
            {
                while (await requestStream.MoveNext())
                {
                    var requestInfo = requestStream.Current;
                    var requestedHeight = requestInfo.NextHeight;
                    var currentHeight = await LightChain.GetCurrentBlockHeightAsync();
                    if (currentHeight - requestedHeight < (ulong)GlobalConfig.InvertibleChainHeight)
                    {
                        await responseStream.WriteAsync(new ResponseSideChainBlockInfo
                        {
                            Success = false
                        });
                        continue;
                    }
                    var blockHeader = await LightChain.GetHeaderByHeightAsync(requestedHeight);
                    var res = new ResponseSideChainBlockInfo
                    {
                        Success = blockHeader != null,
                        BlockInfo = blockHeader == null ? null : new SideChainBlockInfo
                        {
                            Height = requestedHeight,
                            BlockHeaderHash = blockHeader.GetHash(),
                            TransactionMKRoot = blockHeader.MerkleTreeRootOfTransactions,
                            ChainId = blockHeader.ChainId
                        }
                    };
                    
                    await responseStream.WriteAsync(res);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Side chain server out of service with exception.");
            }
        }

        /// <summary>
        /// Response to indexing request from main chain node.
        /// One request to many responses. 
        /// </summary>
        /// <param name="request"></param>
        /// <param name="responseStream"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override async Task IndexServerStreaming(RequestBlockInfo request, 
            IServerStreamWriter<ResponseSideChainBlockInfo> responseStream, ServerCallContext context)
        {
            // TODO: verify the from address and the chain 
            Logger.LogDebug("Side Chain Server received IndexedInfo message.");

            try
            {
                var height = request.NextHeight;
                while (height <= await LightChain.GetCurrentBlockHeightAsync())
                {
                    var blockHeader = await LightChain.GetHeaderByHeightAsync(height);
                    var res = new ResponseSideChainBlockInfo
                    {
                        Success = blockHeader != null,
                        BlockInfo = blockHeader == null ? null : new SideChainBlockInfo
                        {
                            Height = height,
                            BlockHeaderHash = blockHeader.GetHash(),
                            TransactionMKRoot = blockHeader.MerkleTreeRootOfTransactions,
                            ChainId = blockHeader.ChainId
                        }
                    };
                    //Logger.LogLog(LogLevel.Debug, $"Side Chain Server responsed IndexedInfo message of height {height}");
                    await responseStream.WriteAsync(res);
                    height++;
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Exception while index server streaming.");
            }
        }
    }
}