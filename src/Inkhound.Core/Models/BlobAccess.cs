using System;
using Storage.Net;
using Storage.Net.Blobs;

namespace Inkhound.Core.Models;

public enum EBlobProtocol { SMB, LOCAL, FTP }

public class BlobAccess
{
    public string Host { get; set; }

    public EBlobProtocol Protocol { get; set; }

    public string Username { get; set; }
    public string Password { get; set; }

    public string Path { get; set; }


}


