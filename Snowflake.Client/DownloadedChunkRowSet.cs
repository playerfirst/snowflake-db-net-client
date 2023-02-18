using System;
using System.Collections.Generic;

namespace Snowflake.Client
{
    public class DownloadedChunkRowSet
    {
        public Uri ChunkUrl { get; }
        public int ChunkIndex { get; }
        public List<List<string>> ChunkRowSet { get; }
        public string ChunkName { get; }

        public DownloadedChunkRowSet(Uri chunkUrl, int chunkIndex, List<List<string>> chunkRowSet)
        {
            ChunkUrl = chunkUrl;
            ChunkIndex = chunkIndex;
            ChunkRowSet = chunkRowSet;
            ChunkName = "Unknown";
        }

        public override string ToString()
        {
            return $"Index: {ChunkIndex}, Name: {ChunkName}, Rows Count: {ChunkRowSet?.Count}";
        }
    }
}