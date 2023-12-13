// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Core;
using Nethermind.Core.Crypto;

namespace Nethermind.Trie.Pruning
{
    public interface ISmallTrieStore : ITrieNodeResolver, IDisposable
    {
        void CommitNode(long blockNumber, NodeCommitInfo nodeCommitInfo, WriteFlags writeFlags = WriteFlags.None);

        void FinishBlockCommit(TrieType trieType, long blockNumber, TrieNode? root, WriteFlags writeFlags = WriteFlags.None);

        // Only used by snap provider, so ValueHash instead of Hash
        bool IsPersisted(in TreePath path, in ValueHash256 keccak);

        IKeyValueStore AsKeyValueStore();
    }

    public interface ITrieStore : IDisposable
    {
        void CommitNode(long blockNumber, Hash256? address, NodeCommitInfo nodeCommitInfo, WriteFlags writeFlags = WriteFlags.None);

        void FinishBlockCommit(TrieType trieType, long blockNumber, Hash256? address, TrieNode? root, WriteFlags writeFlags = WriteFlags.None);

        bool IsPersisted(Hash256? address, in TreePath path, in ValueHash256 keccak);

        IReadOnlyTrieStore AsReadOnly(IKeyValueStore? keyValueStore);

        event EventHandler<ReorgBoundaryReached>? ReorgBoundaryReached;

        IKeyValueStore AsKeyValueStore(Hash256? address);
        ISmallTrieStore GetTrieStore(Hash256? address);

        TrieNode FindCachedOrUnknown(Hash256? address, in TreePath path, Hash256 hash);
        byte[]? LoadRlp(Hash256? address, in TreePath path, Hash256 hash, ReadFlags flags = ReadFlags.None);
    }
}
