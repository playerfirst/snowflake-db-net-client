using Snowflake.Client.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Snowflake.Client.Extensions;
using Snowflake.Client.Model;

namespace Snowflake.Client
{
    public static class ChunksDownloader
    {
        private const string SSE_C_ALGORITHM = "x-amz-server-side-encryption-customer-algorithm";
        private const string SSE_C_KEY = "x-amz-server-side-encryption-customer-key";
        private const string SSE_C_AES = "AES256";

        private static int _prefetchThreadsCount = 4;
        private static readonly HttpClient Client;

        static ChunksDownloader()
        {
            var httpClientHandler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.None
            };

            Client = new HttpClient(httpClientHandler)
            {
                Timeout = TimeSpan.FromHours(1)
            };
        }

        public static void Configure(ChunksDownloaderOptions options)
        {
            if (options.PrefetchThreadsCount >= 1 && options.PrefetchThreadsCount <= 10)
            {
                _prefetchThreadsCount = options.PrefetchThreadsCount;
            }
        }

        public static async Task<List<List<string>>> DownloadAndParseChunksAsync(ChunksDownloadInfo chunksDownloadInfo, CancellationToken ct = default)
        {
            var chunkHeaders = chunksDownloadInfo.ChunkHeaders;
            var chunksQrmk = chunksDownloadInfo.Qrmk;
            var downloadRequests = chunksDownloadInfo.Chunks.Select(c => BuildChunkDownloadRequest(c, chunkHeaders, chunksQrmk)).ToArray();

            var downloadedChunks = new ConcurrentBag<DownloadedChunkRowSet>();
            await downloadRequests.ForEachWithThrottleAsync(async request =>
                {
                    var chunkRowSet = await GetChunkContentAsync(request, ct).ConfigureAwait(false);
                    var chunkIndex = Array.IndexOf(downloadRequests, request);
                    var rowSet = new DownloadedChunkRowSet(request.RequestUri, chunkIndex, chunkRowSet);
                    downloadedChunks.Add(rowSet);
                }, _prefetchThreadsCount)
                .ConfigureAwait(false);

            var totalRowSet = downloadedChunks.OrderBy(c => c.ChunkIndex).SelectMany(c => c.ChunkRowSet).ToList();
            return totalRowSet;
        }

        [Obsolete("Use DownloadAndParseChunksAsync instead")]
        public static async Task<List<List<string>>> DownloadAndParseChunksSingleThreadAsync(ChunksDownloadInfo chunksDownloadInfo, CancellationToken ct = default)
        {
            var rowSet = new List<List<string>>();

            foreach (var chunk in chunksDownloadInfo.Chunks)
            {
                var downloadRequest = BuildChunkDownloadRequest(chunk, chunksDownloadInfo.ChunkHeaders, chunksDownloadInfo.Qrmk);
                var chunkRowSet = await GetChunkContentAsync(downloadRequest, ct).ConfigureAwait(false);

                rowSet.AddRange(chunkRowSet);
            }

            return rowSet;
        }

        private static HttpRequestMessage BuildChunkDownloadRequest(ExecResponseChunk chunk, Dictionary<string, string> chunkHeaders, string qrmk)
        {
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(chunk.Url)
            };

            if (chunkHeaders != null)
            {
                foreach (var header in chunkHeaders)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
            }
            else
            {
                request.Headers.Add(SSE_C_ALGORITHM, SSE_C_AES);
                request.Headers.Add(SSE_C_KEY, qrmk);
            }

            return request;
        }

        private static async Task<List<List<string>>> GetChunkContentAsync(HttpRequestMessage request, CancellationToken ct = default)
        {
            using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var gzip = new System.IO.Compression.GZipStream(
                        stream, System.IO.Compression.CompressionMode.Decompress);

                    var decompressed = new MemoryStream();
                    gzip.CopyTo(decompressed);
                    decompressed.Position = 0;
                    var reader = new StreamReader(decompressed);
                    var streamText = reader.ReadToEnd();

                    return JsonSerializer.Deserialize<List<List<string>>>($"[{streamText}]");
                }
            }
        }
    }

    public class ChunksDownloaderOptions
    {
        /// <summary>
        /// Sets threads count which will be used to download response data chunks.
        /// See PREFETCH_THREADS_COUNT client variable in SF documentation.
        /// Valid values are: 1 - 10.
        /// Default value: 4.
        /// </summary>
        public int PrefetchThreadsCount { get; set; }
    }
}