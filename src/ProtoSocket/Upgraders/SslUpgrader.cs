using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace ProtoSocket.Upgraders
{
    /// <summary>
    /// Provides an upgrader to upgrade connections to SSL.
    /// </summary>
    public class SslUpgrader : IProtocolUpgrader
    {
        #region Fields
        private X509Certificate2 _cert;
        private string _targetHost;
        #endregion

        #region Properties
        /// <summary>
        /// Gets or sets if the certificate should be verified for revocation.
        /// </summary>
        public bool CheckCertificateRevocation { get; set; } = true;

        /// <summary>
        /// Gets or sets the callback which is called to verify a remote certificate.
        /// </summary>
        public RemoteCertificateValidationCallback RemoteValidationCallback { get; set; } = null;

        /// <summary>
        /// Gets or sets the callback which is called to select a local certificate.
        /// </summary>
        public LocalCertificateSelectionCallback LocalSelectionCallback { get; set; } = null;

        /// <summary>
        /// Gets the certificate.
        /// </summary>
        public X509Certificate2 Certificate {
            get {
                return _cert;
            }
        }

        /// <summary>
        /// Gets the target host.
        /// </summary>
        public string TargetHost {
            get {
                return _targetHost;
            }
        }

        /// <summary>
        /// Gets or sets if the client certificate is required.
        /// </summary>
        public bool ClientCertificateRequired { get; set; } = false;

        /// <summary>
        /// Gets or sets the protocols.
        /// </summary>
        public SslProtocols Protocols { get; set; } = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12;
        #endregion

        #region Methods
        /// <summary>
        /// Upgrades the provided stream.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="peer">The peer.</param>
        /// <returns>The new stream.</returns>
        public async Task<Stream> UpgradeAsync(Stream stream, IProtocolPeer peer) {
            // we need a certificate if we're serverside
            if (_cert == null && peer.Side == ProtocolSide.Server)
                throw new InvalidOperationException("The server connection cannot upgrade to SSL without a certificate");
            else if (_targetHost == null && peer.Side == ProtocolSide.Client)
                throw new InvalidOperationException("The client connection cannot upgrade to SSL without a target hostname");

            // authenticate
            SslStream sslStream = new SslStream(stream, true, RemoteValidationCallback, LocalSelectionCallback);

            if (peer.Side == ProtocolSide.Server)
                await sslStream.AuthenticateAsServerAsync(_cert, ClientCertificateRequired, Protocols, CheckCertificateRevocation).ConfigureAwait(false);
            else
                await sslStream.AuthenticateAsClientAsync(_targetHost, new X509CertificateCollection(), Protocols, CheckCertificateRevocation).ConfigureAwait(false);

            return sslStream;
        }
        #endregion

        #region Constructors
        /// <summary>
        /// Creates an upgrader with no certificate and the target hostname.
        /// </summary>
        /// <param name="host">The target host.</param>
        public SslUpgrader(string host) {
            _targetHost = host;
        }

        /// <summary>
        /// Creates an upgrader with a certificate.
        /// </summary>
        /// <param name="cert">The certificate.</param>
        public SslUpgrader(X509Certificate2 cert) {
            _cert = cert;
        }
        #endregion
    }
}