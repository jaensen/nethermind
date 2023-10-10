// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using Nethermind.Abi;
using Nethermind.Consensus.AuRa.Contracts;
using Nethermind.Consensus.Rewards;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.Specs.ChainSpecStyle;

namespace Nethermind.Consensus.AuRa.Rewards
{
    public class AuRaRewardCalculator : IRewardCalculator
    {
        private readonly StaticRewardCalculator _blockRewardCalculator;
        private readonly IList<IRewardContract> _contracts;
        private readonly ISpecProvider _specProvider;

        public AuRaRewardCalculator(AuRaParameters auRaParameters, IAbiEncoder abiEncoder, ITransactionProcessor transactionProcessor, ISpecProvider specProvider)
        {
            if (auRaParameters is null) throw new ArgumentNullException(nameof(auRaParameters));
            if (abiEncoder is null) throw new ArgumentNullException(nameof(abiEncoder));
            if (transactionProcessor is null) throw new ArgumentNullException(nameof(transactionProcessor));

            _specProvider = specProvider ?? throw new ArgumentNullException(nameof(specProvider));

            IList<IRewardContract> BuildTransitions()
            {
                var contracts = new List<IRewardContract>();

                if (auRaParameters.BlockRewardContractTransitions is not null)
                {
                    contracts.AddRange(auRaParameters.BlockRewardContractTransitions.Select(t => new RewardContract(transactionProcessor, abiEncoder, t.Value, t.Key, _specProvider)));
                    contracts.Sort((a, b) => a.Activation.CompareTo(b.Activation));
                }

                if (auRaParameters.BlockRewardContractAddress is not null)
                {
                    var contractTransition = auRaParameters.BlockRewardContractTransition ?? 0;
                    if (contractTransition > (contracts.FirstOrDefault()?.Activation ?? long.MaxValue))
                    {
                        throw new ArgumentException($"{nameof(auRaParameters.BlockRewardContractTransition)} provided for {nameof(auRaParameters.BlockRewardContractAddress)} is higher than first {nameof(auRaParameters.BlockRewardContractTransitions)}.");
                    }

                    contracts.Insert(0, new RewardContract(transactionProcessor, abiEncoder, auRaParameters.BlockRewardContractAddress, contractTransition, _specProvider));
                }

                return contracts;
            }

            if (auRaParameters is null) throw new ArgumentNullException(nameof(AuRaParameters));
            _contracts = BuildTransitions();
            _blockRewardCalculator = new StaticRewardCalculator(auRaParameters.BlockReward);
        }

        public BlockReward[] CalculateRewards(Block block)
        {
            if (block.IsGenesis)
            {
                return Array.Empty<BlockReward>();
            }

            return _contracts.TryGetForBlock(block.Number, out var contract)
                ? CalculateRewardsWithContract(block, contract)
                : _blockRewardCalculator.CalculateRewards(block);
        }


        private BlockReward[] CalculateRewardsWithContract(Block block, IRewardContract contract)
        {
            (Address[] beneficieries, ushort[] kinds) GetBeneficiaries()
            {
                var length = block.Uncles.Length + 1;

                Address[] beneficiariesList = new Address[length];
                ushort[] kindsList = new ushort[length];
                beneficiariesList[0] = block.Beneficiary;
                kindsList[0] = BenefactorKind.Author;

                for (int i = 0; i < block.Uncles.Length; i++)
                {
                    var uncle = block.Uncles[i];
                    if (BenefactorKind.TryGetUncle(block.Number - uncle.Number, out var kind))
                    {
                        beneficiariesList[i + 1] = uncle.Beneficiary;
                        kindsList[i + 1] = kind;
                    }
                }

                return (beneficiariesList, kindsList);
            }

            var (beneficiaries, kinds) = GetBeneficiaries();
            var (addresses, rewards) = contract.Reward(block.Header, beneficiaries, kinds);

            var blockRewards = new BlockReward[addresses.Length];
            for (int index = 0; index < addresses.Length; index++)
            {
                var address = addresses[index];
                blockRewards[index] = new BlockReward(address, rewards[index], BlockRewardType.External);
            }

            return blockRewards;
        }

        public static IRewardCalculatorSource GetSource(AuRaParameters auRaParameters, IAbiEncoder abiEncoder, ISpecProvider specProvider) => new AuRaRewardCalculatorSource(auRaParameters, abiEncoder, specProvider);

        private class AuRaRewardCalculatorSource : IRewardCalculatorSource
        {
            private readonly AuRaParameters _auRaParameters;
            private readonly IAbiEncoder _abiEncoder;
            private readonly ISpecProvider _specProvider;

            public AuRaRewardCalculatorSource(AuRaParameters auRaParameters, IAbiEncoder abiEncoder, ISpecProvider specProvider)
            {
                _auRaParameters = auRaParameters;
                _abiEncoder = abiEncoder;
                _specProvider = specProvider;
            }

            public IRewardCalculator Get(ITransactionProcessor processor) => new AuRaRewardCalculator(_auRaParameters, _abiEncoder, processor, _specProvider);
        }

        public static class BenefactorKind
        {
            public const ushort Author = 0;
            public const ushort EmptyStep = 2;
            public const ushort External = 3;
            private const ushort uncleOffset = 100;
            private const ushort minDistance = 1;
            private const ushort maxDistance = 6;

            public static bool TryGetUncle(long distance, out ushort kind)
            {
                if (IsValidDistance(distance))
                {
                    kind = (ushort)(uncleOffset + distance);
                    return true;
                }

                kind = 0;
                return false;
            }

            public static BlockRewardType ToBlockRewardType(ushort kind)
            {
                switch (kind)
                {
                    case Author:
                        return BlockRewardType.Block;
                    case External:
                        return BlockRewardType.External;
                    case EmptyStep:
                        return BlockRewardType.EmptyStep;
                    case ushort uncle when IsValidDistance(uncle - uncleOffset):
                        return BlockRewardType.Uncle;
                    default:
                        throw new ArgumentException($"Invalid BlockRewardType for kind {kind}", nameof(kind));
                }
            }

            private static bool IsValidDistance(long distance)
            {
                return distance >= minDistance && distance <= maxDistance;
            }
        }
    }
}
