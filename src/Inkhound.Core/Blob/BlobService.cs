using System.Net.Sockets;
using Inkhound.Core.Models;
using Storage.Net;
using Storage.Net.Blobs;
using NetBlob = Storage.Net.Blobs.Blob;

namespace Inkhound.Core.Blob;

public enum BlobServiceStatus
{
    NotInitialized,
    Connected,
    PathNotFound,
    AuthError,
    NetworkError,
    Error
}

public class BlobService
{
    public BlobAccess blobAccess { get; set; }
    public IBlobStorage? blobStorage { get; set; }
    public BlobServiceStatus Status { get; private set; } = BlobServiceStatus.NotInitialized;
    public string? StatusMessage { get; private set; }


    public BlobService(BlobAccess blob)
    {
        blobAccess = blob;
    }

    public async Task<bool> Init()
    {
        var connectionstring = getConnectionString();
        if (string.IsNullOrEmpty(connectionstring))
        {
            Status = BlobServiceStatus.Error;
            StatusMessage = "Invalid connection configuration.";
            return false;
        }

        try
        {
            if (blobAccess.Protocol == EBlobProtocol.LOCAL && !Directory.Exists(blobAccess.Path))
            {
                Status = BlobServiceStatus.PathNotFound;
                StatusMessage = $"Path '{blobAccess.Path}' does not exist.";
                return false;
            }

            blobStorage = StorageFactory.Blobs.FromConnectionString(connectionstring);

            if (blobAccess.Protocol != EBlobProtocol.LOCAL)
            {
                var exists = await blobStorage.ExistsAsync("/");
                if (!exists)
                {
                    Status = BlobServiceStatus.PathNotFound;
                    StatusMessage = $"Path '{blobAccess.Path}' does not exist.";
                    return false;
                }
            }

            Status = BlobServiceStatus.Connected;
            StatusMessage = null;
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            Status = BlobServiceStatus.AuthError;
            StatusMessage = ex.Message;
            return false;
        }
        catch (Exception ex) when (ex is SocketException || ex is IOException)
        {
            Status = BlobServiceStatus.NetworkError;
            StatusMessage = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            Status = BlobServiceStatus.Error;
            StatusMessage = ex.Message;
            return false;
        }
    }

    private string getConnectionString()
    {
        if (blobAccess != null)
        {
            var pass = Uri.EscapeDataString(blobAccess.Password);
            switch (blobAccess.Protocol)
            {
                case EBlobProtocol.SMB:
                    return $"smb://{blobAccess.Username}:{pass}@{blobAccess.Host}/{blobAccess.Path}";
                case EBlobProtocol.LOCAL:
                    return $"disk://path={blobAccess.Path}";
                case EBlobProtocol.FTP:
                    return $"ftp://host={blobAccess.Host};user={blobAccess.Username};password={pass}";
            }

        }
        return "";
    }


    public async Task<List<NetBlob>> GetDirectoriesAsync(string? path = null, bool recursive = false)
    {
        if (blobStorage is null) throw new InvalidOperationException("Call Init() before using BlobService.");
        var options = new ListOptions
        {
            FolderPath = path ?? "/",
            Recurse = recursive,
            BrowseFilter = b => b.Kind == BlobItemKind.Folder
        };
        var result = await blobStorage.ListAsync(options);
        return [.. result];
    }

    public async Task<List<NetBlob>> GetFilesAsync(
        string? path = null,
        string? prefix = null,
        bool recursive = false,
        IEnumerable<string>? extensions = null)
    {
        Func<NetBlob, bool> filter = b => b.Kind == BlobItemKind.File;

        if (extensions != null)
        {
            var extSet = extensions
                .Select(e => e.StartsWith('.') ? e : $".{e}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            filter = b => b.Kind == BlobItemKind.File &&
                          extSet.Contains(Path.GetExtension(b.Name));
        }

        if (blobStorage is null) throw new InvalidOperationException("Call Init() before using BlobService.");
        var options = new ListOptions
        {
            FolderPath = path ?? "/",
            Recurse = recursive,
            FilePrefix = prefix,
            BrowseFilter = filter
        };
        var result = await blobStorage.ListAsync(options);
        return [.. result];
    }

}
