namespace ovudb.SystemDatabase.Models;

/// <summary>
/// System table slave_master_info - master info for replication
/// </summary>
public class SystemSlaveMasterInfo
{
    public int Id { get; set; }
    public int NumberOfLines { get; set; }
    public string MasterLogName { get; set; } = string.Empty;
    public long MasterLogPos { get; set; }
    public string Host { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserPassword { get; set; } = string.Empty;
    public int Port { get; set; }
    public int ConnectRetry { get; set; } = 60;
    public bool EnabledSsl { get; set; } = false;
    public string SslCa { get; set; } = string.Empty;
    public string SslCapath { get; set; } = string.Empty;
    public string SslCert { get; set; } = string.Empty;
    public string SslCipher { get; set; } = string.Empty;
    public string SslKey { get; set; } = string.Empty;
    public bool SslVerifyServerCert { get; set; } = false;
    public double Heartbeat { get; set; } = 30.0;
    public string Bind { get; set; } = string.Empty;
    public string IgnoredServerIds { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public long RetryCount { get; set; } = 86400;
    public string SslCrl { get; set; } = string.Empty;
    public string SslCrlpath { get; set; } = string.Empty;
    public bool EnabledAutoPosition { get; set; } = false;
    public string ChannelName { get; set; } = string.Empty;
    public string TlsVersion { get; set; } = string.Empty;
    public string PublicKeyPath { get; set; } = string.Empty;
    public bool GetPublicKey { get; set; } = false;
    public string NetworkNamespace { get; set; } = string.Empty;
    public int MasterCompressionAlgorithm { get; set; } = 0;
    public string MasterZstdCompressionLevel { get; set; } = string.Empty;
    public int TlsCiphersuites { get; set; } = 0;
    public string SourceConnectionAutoFailover { get; set; } = string.Empty;
    public int GtidOnly { get; set; } = 0;
}
