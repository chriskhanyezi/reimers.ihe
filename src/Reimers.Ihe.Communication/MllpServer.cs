﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="MllpServer.cs" company="Reimers.dk">
//   Copyright © Reimers.dk 2017
//   This source is subject to the MIT License.
//   Please see https://opensource.org/licenses/MIT for details.
//   All other rights reserved.
//
//   The above copyright notice and this permission notice shall be included in
//   all copies or substantial portions of the Software.
//
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//   IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//   FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//   LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//   OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//   THE SOFTWARE.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Reimers.Ihe.Communication
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Security;
    using System.Net.Sockets;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using NHapi.Base.Parser;

    /// <summary>
    /// Defines an IHE server using MLLP connections.
    /// </summary>
    public class MllpServer : IDisposable
    {
        private readonly IMessageLog _messageLog;
        private readonly IHl7MessageMiddleware _middleware;
        private readonly PipeParser? _parser;
        private readonly Encoding _encoding;
        private readonly X509Certificate? _serverCertificate;
        private readonly RemoteCertificateValidationCallback? _userCertificateValidationCallback;
        private readonly TcpListener _listener;
        private readonly List<MllpHost> _connections = new List<MllpHost>();
        private readonly Timer _timer;
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();
#pragma warning disable IDE0052 // Remove unread private members
        private Task? _readTask;
#pragma warning restore IDE0052 // Remove unread private members

        /// <summary>
        /// Initializes a new instance of the <see cref="MllpServer"/> class.
        /// </summary>
        /// <param name="endPoint">The <see cref="IPEndPoint"/> the server will listen on.</param>
        /// <param name="messageLog">The <see cref="IMessageLog"/> to use for logging incoming messages.</param>
        /// <param name="middleware">The message handling middleware.</param>
        /// <param name="cleanupInterval">The interval between cleaning up client connections.</param>
        /// <param name="parser">The <see cref="PipeParser"/> to use for parsing and encoding.</param>
        /// <param name="encoding">The <see cref="Encoding"/> to use for network transfers.</param>
        /// <param name="serverCertificate">The certificates to use for secure connections.</param>
        /// <param name="userCertificateValidationCallback">Optional certificate validation callback.</param>
        public MllpServer(
            IPEndPoint endPoint,
            IMessageLog messageLog,
            IHl7MessageMiddleware middleware,
            TimeSpan cleanupInterval = default,
            PipeParser? parser = null,
            Encoding? encoding = null,
            X509Certificate? serverCertificate = null,
            RemoteCertificateValidationCallback? userCertificateValidationCallback = null)
        {
            _messageLog = messageLog;
            _middleware = middleware;
            _parser = parser;
            _encoding = encoding ?? Encoding.ASCII;
            _serverCertificate = serverCertificate;
            _userCertificateValidationCallback = userCertificateValidationCallback;
            _listener = new TcpListener(endPoint);
            cleanupInterval = cleanupInterval == default
                ? TimeSpan.FromSeconds(5)
                : cleanupInterval;
            _timer = new Timer(
                CleanConnections,
                null,
                cleanupInterval,
                cleanupInterval);
        }

        /// <summary>
        /// Starts the server
        /// </summary>
        public void Start()
        {
            _listener.Start();
            _readTask = Read();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _tokenSource.Cancel();
            _tokenSource.Dispose();
            _listener.Stop();
            _timer.Dispose();
            foreach (var connection in _connections)
            {
                connection.Dispose();
            }
            CleanConnections(null);
        }

        private async Task Read()
        {
            while (!_tokenSource.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync()
                        .ConfigureAwait(false);
                    var connection = await MllpHost.Create(
                            client,
                            _messageLog,
                            _middleware,
                            _parser,
                            _encoding,
                            _serverCertificate,
                            _userCertificateValidationCallback)
                        .ConfigureAwait(false);
                    lock (_connections)
                    {
                        _connections.Add(connection);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Ignore
                }
            }
        }

        private void CleanConnections(object? o)
        {
            MllpHost[] temp;
            lock (_connections)
            {
                temp = _connections.Where(x => !x.IsConnected).ToArray();
                foreach (var conn in temp)
                {
                    _connections.Remove(conn);
                }
            }

            foreach (var host in temp)
            {
                host.Dispose();
            }
        }
    }
}
