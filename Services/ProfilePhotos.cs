using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace ApprovalAdministrationTool.Services
{
    /// <summary>
    /// People's Microsoft 365 profile photos (96×96, from Microsoft Graph), kept for the session so each is read once.
    /// Someone without a photo (or whose photo cannot be read) is remembered as having none.
    /// </summary>
    internal sealed class ProfilePhotos
    {
        public const string GraphResource = "https://graph.microsoft.com";

        private static HttpClient http = new HttpClient { BaseAddress = new Uri(GraphResource + "/v1.0/"), Timeout = TimeSpan.FromSeconds(15) };

        private readonly ConcurrentDictionary<Guid, byte[]> photos = new ConcurrentDictionary<Guid, byte[]>();

        /// <summary>The photo already read for an Entra ID user, or null (none, or not read yet).</summary>
        public byte[] Find(Guid? entraObjectId)
        {
            return entraObjectId.HasValue && photos.TryGetValue(entraObjectId.Value, out var photo) ? photo : null;
        }

        /// <summary>Whether every one of these users' photos has been looked for already.</summary>
        public bool HasAll(IEnumerable<Guid?> entraObjectIds)
        {
            return entraObjectIds.Where(id => id.HasValue).All(id => photos.ContainsKey(id.Value));
        }

        /// <summary>Reads the photos not looked for yet, all at once, with a Microsoft Graph token.</summary>
        public async Task LoadAsync(IEnumerable<Guid?> entraObjectIds, string graphToken)
        {
            var missing = entraObjectIds.Where(id => id.HasValue && !photos.ContainsKey(id.Value)).Select(id => id.Value).Distinct().ToList();
            await Task.WhenAll(missing.Select(async id => photos[id] = await ReadAsync(id, graphToken).ConfigureAwait(false))).ConfigureAwait(false);
        }

        private static async Task<byte[]> ReadAsync(Guid entraObjectId, string graphToken)
        {
            try
            {
                using (var message = new HttpRequestMessage(HttpMethod.Get, "users/" + entraObjectId + "/photos/96x96/$value"))
                {
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphToken);
                    using (var response = await http.SendAsync(message).ConfigureAwait(false))
                    {
                        return response.StatusCode == HttpStatusCode.OK
                            ? await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false)
                            : null; // 404: no photo; 403: not allowed to read it
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                return null;
            }
        }
    }
}
