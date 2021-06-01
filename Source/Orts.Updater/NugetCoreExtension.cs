using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using NuGet.Common;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace Orts.Updater
{
    public static class NugetCoreExtension
    {
        public static async Task<long> PackageSize(this SourceRepository sourceRepository, PackageIdentity packageIdentity, CancellationToken cancellationToken)
        {
            if (null == sourceRepository || null == packageIdentity)
                return 0;

            using (SourceCacheContext cache = new SourceCacheContext
            {
                DirectDownload = true,
                NoCache = true
            })
            {
                try
                {
                    RegistrationResourceV3 registrationResource = await sourceRepository.GetResourceAsync<RegistrationResourceV3>(cancellationToken).ConfigureAwait(false);
                    JObject packageMetadata = await registrationResource.GetPackageMetadata(packageIdentity, cache, NullLogger.Instance, cancellationToken).ConfigureAwait(false);

                    if (null == packageMetadata)
                    {
                        throw new PackageNotFoundProtocolException(packageIdentity);
                    }
                    string catalogItemUrl = packageMetadata.Value<string>("@id");

                    HttpSourceResource httpSourceResource = await sourceRepository.GetResourceAsync<HttpSourceResource>(cancellationToken).ConfigureAwait(false);
                    JObject catalogItem = await httpSourceResource.HttpSource.GetJObjectAsync(new HttpSourceRequest(catalogItemUrl, NullLogger.Instance), NullLogger.Instance, cancellationToken).ConfigureAwait(false);
                    return catalogItem.Value<long>("packageSize");
                }
                catch (Exception ex) when (ex is FatalProtocolException|| ex is HttpRequestException)
                {
                    return 0;
                }
            }
        }
    }

    internal class ProgressMemoryStream : MemoryStream
    {
        public event EventHandler<ProgressChangedEventArgs> ProgressChanged;
        public long ExpectedLength { get; set; }
        private int percentage;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Position < 350) //quite some hack but seems the first 314 bytes are never read (Position is never 0
                percentage = 0;
            if (base.CanRead && (Position / (Length / 100)) > percentage)
            {
                ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(percentage = (int)(Position / (Length / 100)), null));
            }
            return base.Read(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (base.CanWrite && ExpectedLength > 0 && (base.Position + buffer.Length) / (ExpectedLength / 100) > percentage)
            {
                ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(percentage = (int)((base.Position + buffer.Length) / (ExpectedLength / 100)), null));
            }
            return base.WriteAsync(buffer, offset, count, cancellationToken); ;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
        {
            if (base.CanWrite && ExpectedLength > 0 && (base.Position + source.Length) / (ExpectedLength / 100) > percentage)
            {
                ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(percentage = (int)((base.Position + source.Length) / (ExpectedLength / 100)), null));
            }
            return base.WriteAsync(source, cancellationToken);
        }
    }


}
