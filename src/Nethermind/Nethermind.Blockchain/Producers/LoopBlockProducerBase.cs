//  Copyright (c) 2021 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Blockchain.Processing;
using Nethermind.Consensus;
using Nethermind.Consensus.Transactions;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Logging;
using Nethermind.State;

namespace Nethermind.Blockchain.Producers
{
    public abstract class LoopBlockProducerBase : BlockProducerBase
    {
        private const int ChainNotYetProcessedMillisecondsDelay = 100;
        private readonly string _name;
        private Task? _producerTask;
        private bool _isRunning;
        
        protected CancellationTokenSource LoopCancellationTokenSource { get; } = new();
        protected int _canProduce;

        protected LoopBlockProducerBase(
            ITxSource txSource,
            IBlockchainProcessor processor,
            ISealer sealer,
            IBlockTree blockTree,
            IBlockProcessingQueue blockProcessingQueue,
            IStateProvider stateProvider,
            ITimestamper timestamper,
            IGasLimitCalculator gasLimitCalculator,
            ISpecProvider specProvider,
            ILogManager logManager,
            string name) 
            : base(
                txSource,
                processor,
                sealer,
                blockTree,
                blockProcessingQueue,
                stateProvider,
                gasLimitCalculator,
                timestamper,
                specProvider,
                logManager)
        {
            _name = name;
        }

        public override void Start()
        {
            BlockProcessingQueue.ProcessingQueueEmpty += OnBlockProcessorQueueEmpty;
            BlockTree.NewBestSuggestedBlock += BlockTreeOnNewBestSuggestedBlock;
            _isRunning = true;
            
            _producerTask = Task.Run(ProducerLoop, LoopCancellationTokenSource.Token).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    if (Logger.IsError) Logger.Error($"{_name} block producer encountered an exception.", t.Exception);
                }
                else if (t.IsCanceled)
                {
                    if (Logger.IsDebug) Logger.Debug($"{_name} block producer stopped.");
                }
                else if (t.IsCompleted)
                {
                    if (Logger.IsDebug) Logger.Debug($"{_name} block producer complete.");
                }
            });
        }

        public override async Task StopAsync()
        {
            BlockProcessingQueue.ProcessingQueueEmpty -= OnBlockProcessorQueueEmpty;
            BlockTree.NewBestSuggestedBlock -= BlockTreeOnNewBestSuggestedBlock;
            _isRunning = false;
            
            LoopCancellationTokenSource.Cancel();
            await (_producerTask ?? Task.CompletedTask);
        }

        protected override bool IsRunning() => _producerTask != null && _isRunning;
        
        protected virtual async ValueTask ProducerLoop()
        {
            _lastProducedBlockDateTime = DateTime.UtcNow;
            while (!LoopCancellationTokenSource.IsCancellationRequested)
            {
                if (_canProduce == 1 && BlockProcessingQueue.IsEmpty)
                {
                    try
                    {
                        await ProducerLoopStep(LoopCancellationTokenSource.Token);
                    }
                    catch (Exception e) when(!(e is TaskCanceledException))
                    {
                        if (Logger.IsError) { Logger.Error("Failed to produce block.", e); }

                        Metrics.FailedBlockSeals++;
                        throw;
                    }
                }
                else
                {
                    if (Logger.IsDebug)
                        Logger.Debug(
                            $"Delaying producing block, chain not processed yet. BlockProcessingQueue count {BlockProcessingQueue.Count}.");
                    await Task.Delay(ChainNotYetProcessedMillisecondsDelay, LoopCancellationTokenSource.Token);
                }
            }
        }

        protected virtual async ValueTask ProducerLoopStep(CancellationToken cancellationToken)
        {
            await TryProduceNewBlock(cancellationToken);
        }
        
        private void BlockTreeOnNewBestSuggestedBlock(object sender, BlockEventArgs e)
        {
            if (BlockTree.Head?.Hash != e.Block?.Hash)
            {
                Interlocked.Exchange(ref _canProduce, 0);
                Interlocked.Exchange(ref Metrics.CanProduceBlocks, 0);
                if (Logger.IsTrace)
                    Logger.Trace(
                        $"Can not produce a block new best suggested {BlockTree.BestSuggestedHeader?.ToString(BlockHeader.Format.FullHashAndNumber)}" +
                        $"{Environment.NewLine}{new StackTrace()}");
            }
            else
            {
                Interlocked.Exchange(ref Metrics.CanProduceBlocks, 1);
                if (Logger.IsTrace)
                    Logger.Trace(
                        $"Can produce blocks, a block new best suggested {BlockTree.BestSuggestedHeader?.ToString(BlockHeader.Format.FullHashAndNumber)}" +
                        $"{Environment.NewLine}{new StackTrace()} is already processed.");
            }
        }

        private void OnBlockProcessorQueueEmpty(object sender, EventArgs e)
        {
            Interlocked.Exchange(ref _canProduce, 1);
            Interlocked.Exchange(ref Metrics.CanProduceBlocks, 1);
            if (Logger.IsTrace)
                Logger.Trace(
                    $"Can produce blocks, current best suggested {BlockTree.BestSuggestedHeader}" +
                    $"{Environment.NewLine}current head {BlockTree.Head}{Environment.NewLine}{new StackTrace()}");        
        }
    }
}
