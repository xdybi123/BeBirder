using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace EarPicker
{
    /// <summary>
    /// A tiny hand-rolled HTTP server that publishes the live JPEG frames as
    /// an MJPEG stream (multipart/x-mixed-replace) on GET /. Deliberately
    /// minimal - just enough HTTP to serve a browser or VLC.
    /// </summary>
    sealed class MjpegPublisher
    {
        const int MaximumStreamingClients = 4;
        const int MaximumConnections = 16;
        const int MaximumRequestBytes = 8192;
        const int RequestTimeoutMilliseconds = 2000;

        readonly object lifecycleLock = new object();
        RunContext current;

        sealed class RunContext
        {
            public readonly TcpListener Listener;
            public readonly object ClientLock = new object();
            public readonly HashSet<TcpClient> Clients = new HashSet<TcpClient>();
            public readonly HashSet<Thread> ClientThreads = new HashSet<Thread>();
            public readonly object FrameLock = new object();
            public volatile bool Stopping;
            public Thread AcceptThread;
            public byte[] LatestFrame;
            public int Sequence;
            public int StreamingClients;

            public RunContext(TcpListener listener) { Listener = listener; }
        }

        enum RequestResult
        {
            Valid,
            BadRequest,
            MethodNotAllowed,
            NotFound,
            Busy
        }

        public void Start(int port)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException("port");

            lock (lifecycleLock)
            {
                StopCurrent();

                TcpListener listener = new TcpListener(IPAddress.Any, port);
                RunContext context = null;
                try
                {
                    listener.Start();
                    context = new RunContext(listener);
                    Thread acceptThread = new Thread(new ThreadStart(delegate { AcceptLoop(context); }));
                    acceptThread.IsBackground = true;
                    acceptThread.Name = "MJPEG publisher";
                    context.AcceptThread = acceptThread;
                    acceptThread.Start();
                    current = context;
                }
                catch
                {
                    if (context != null)
                    {
                        context.Stopping = true;
                        context.AcceptThread = null;
                    }
                    try { listener.Stop(); } catch { }
                    throw;
                }
            }
        }

        public void Stop()
        {
            lock (lifecycleLock) { StopCurrent(); }
        }

        public void SetFrame(byte[] jpeg)
        {
            if (jpeg == null) return;

            RunContext context;
            lock (lifecycleLock) { context = current; }
            if (context == null || context.Stopping) return;

            lock (context.FrameLock)
            {
                if (context.Stopping) return;
                context.LatestFrame = jpeg;
                unchecked { context.Sequence++; }
                Monitor.PulseAll(context.FrameLock);
            }
        }

        public void ClearFrame()
        {
            RunContext context;
            lock (lifecycleLock) { context = current; }
            if (context == null || context.Stopping) return;

            lock (context.FrameLock)
            {
                if (context.Stopping) return;
                context.LatestFrame = null;
                unchecked { context.Sequence++; }
                Monitor.PulseAll(context.FrameLock);
            }
        }

        void StopCurrent()
        {
            RunContext context = current;
            current = null;
            if (context == null) return;

            context.Stopping = true;
            try { context.Listener.Stop(); } catch { }
            lock (context.FrameLock) { Monitor.PulseAll(context.FrameLock); }

            CloseTrackedClients(context);

            List<TcpClient> clients;
            List<Thread> workers;
            lock (context.ClientLock)
            {
                clients = new List<TcpClient>(context.Clients);
                workers = new List<Thread>(context.ClientThreads);
            }

            foreach (TcpClient client in clients) CloseClient(client);
            lock (context.FrameLock) { context.LatestFrame = null; }

            // Join everything on a background thread so Stop() itself never blocks the caller.
            StartBackgroundCleanup(context.AcceptThread, workers);
        }

        void AcceptLoop(RunContext context)
        {
            while (!context.Stopping)
            {
                TcpClient client;
                try { client = context.Listener.AcceptTcpClient(); }
                catch (SocketException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (InvalidOperationException) { break; }

                Thread worker = null;
                lock (context.ClientLock)
                {
                    if (!context.Stopping && context.ClientThreads.Count < MaximumConnections)
                    {
                        TcpClient acceptedClient = client;
                        worker = new Thread(new ThreadStart(delegate { Serve(context, acceptedClient); }));
                        worker.IsBackground = true;
                        worker.Name = "MJPEG client";
                        context.Clients.Add(client);
                        context.ClientThreads.Add(worker);
                    }
                }

                if (worker == null)
                {
                    CloseClient(client);
                    continue;
                }

                try { worker.Start(); }
                catch
                {
                    lock (context.ClientLock)
                    {
                        context.Clients.Remove(client);
                        context.ClientThreads.Remove(worker);
                    }
                    CloseClient(client);
                }
            }
        }

        void Serve(RunContext context, TcpClient client)
        {
            bool streamingSlot = false;
            try
            {
                client.NoDelay = true;
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                client.ReceiveTimeout = RequestTimeoutMilliseconds;
                client.SendTimeout = 3000;
                NetworkStream stream = client.GetStream();

                RequestResult requestResult = ReadRequest(stream, "/");
                if (requestResult != RequestResult.Valid)
                {
                    WriteError(stream, requestResult);
                    return;
                }

                bool busy;
                lock (context.ClientLock)
                {
                    if (context.Stopping) return;
                    busy = context.StreamingClients >= MaximumStreamingClients;
                    if (!busy) { context.StreamingClients++; streamingSlot = true; }
                }
                if (busy) { WriteError(stream, RequestResult.Busy); return; }

                byte[] header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Connection: close\r\n" +
                    "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
                    "Pragma: no-cache\r\n" +
                    "Access-Control-Allow-Origin: *\r\n" +
                    "X-Content-Type-Options: nosniff\r\n" +
                    "Content-Type: multipart/x-mixed-replace; boundary=frame\r\n\r\n");
                stream.Write(header, 0, header.Length);

                int sentSequence = -1;
                while (!context.Stopping)
                {
                    byte[] frame;
                    int sequence;
                    lock (context.FrameLock)
                    {
                        if (!context.Stopping && (context.LatestFrame == null || context.Sequence == sentSequence))
                            Monitor.Wait(context.FrameLock, 500);

                        if (context.Stopping) break;
                        if (context.LatestFrame == null || context.Sequence == sentSequence)
                        {
                            frame = null;
                            sequence = sentSequence;
                        }
                        else
                        {
                            frame = context.LatestFrame;
                            sequence = context.Sequence;
                        }
                    }

                    if (frame == null)
                    {
                        if (ClientDisconnected(client)) break;
                        continue;
                    }

                    byte[] part = Encoding.ASCII.GetBytes(
                        "--frame\r\nContent-Type: image/jpeg\r\nContent-Length: " + frame.Length + "\r\n\r\n");
                    stream.Write(part, 0, part.Length);
                    stream.Write(frame, 0, frame.Length);
                    byte[] end = { 13, 10 };
                    stream.Write(end, 0, end.Length);
                    stream.Flush();
                    sentSequence = sequence;
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(DateTime.Now.ToString("s") + " MJPEG client: " + ex); }
            finally
            {
                lock (context.ClientLock)
                {
                    if (streamingSlot && context.StreamingClients > 0) context.StreamingClients--;
                    context.Clients.Remove(client);
                    context.ClientThreads.Remove(Thread.CurrentThread);
                }
                CloseClient(client);
            }
        }

        static RequestResult ReadRequest(NetworkStream stream, string expectedPath)
        {
            byte[] request = new byte[MaximumRequestBytes];
            int count = 0, headerEnd = -1;
            System.Diagnostics.Stopwatch timeout = System.Diagnostics.Stopwatch.StartNew();

            while (headerEnd < 0 && count < request.Length)
            {
                int remaining = RequestTimeoutMilliseconds - (int)timeout.ElapsedMilliseconds;
                if (remaining <= 0) return RequestResult.BadRequest;
                stream.ReadTimeout = remaining;

                int previousCount = count;
                int read = stream.Read(request, count, request.Length - count);
                if (read <= 0) return RequestResult.BadRequest;
                count += read;
                headerEnd = FindHeaderEnd(request, count, Math.Max(3, previousCount - 3));
            }
            if (headerEnd < 0) return RequestResult.BadRequest;

            string text = Encoding.ASCII.GetString(request, 0, count);
            int lineEnd = text.IndexOf("\r\n", StringComparison.Ordinal);
            if (lineEnd <= 0) return RequestResult.BadRequest;

            string[] parts = text.Substring(0, lineEnd).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) return RequestResult.BadRequest;
            if (!String.Equals(parts[0], "GET", StringComparison.Ordinal)) return RequestResult.MethodNotAllowed;
            if (!String.Equals(parts[1], expectedPath, StringComparison.Ordinal)) return RequestResult.NotFound;
            if (!String.Equals(parts[2], "HTTP/1.0", StringComparison.Ordinal) &&
                !String.Equals(parts[2], "HTTP/1.1", StringComparison.Ordinal))
                return RequestResult.BadRequest;

            return RequestResult.Valid;
        }

        static int FindHeaderEnd(byte[] request, int count, int start)
        {
            for (int i = Math.Max(3, start); i < count; i++)
                if (request[i - 3] == 13 && request[i - 2] == 10 && request[i - 1] == 13 && request[i] == 10)
                    return i + 1;
            return -1;
        }

        static void WriteError(NetworkStream stream, RequestResult result)
        {
            string status;
            string extra = "";
            if (result == RequestResult.MethodNotAllowed)
            {
                status = "405 Method Not Allowed";
                extra = "Allow: GET\r\n";
            }
            else if (result == RequestResult.NotFound)
            {
                status = "404 Not Found";
            }
            else if (result == RequestResult.Busy)
            {
                status = "503 Service Unavailable";
                extra = "Retry-After: 1\r\n";
            }
            else
            {
                status = "400 Bad Request";
            }

            byte[] response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 " + status + "\r\n" +
                "Connection: close\r\n" +
                "Cache-Control: no-store\r\n" +
                extra +
                "Content-Length: 0\r\n\r\n");
            stream.Write(response, 0, response.Length);
            stream.Flush();
        }

        static void CloseTrackedClients(RunContext context)
        {
            List<TcpClient> clients;
            lock (context.ClientLock) { clients = new List<TcpClient>(context.Clients); }
            foreach (TcpClient client in clients) CloseClient(client);
        }

        static void CloseClient(TcpClient client)
        {
            if (client != null) try { client.Close(); } catch { }
        }

        static bool ClientDisconnected(TcpClient client)
        {
            try
            {
                Socket socket = client.Client;
                return socket.Poll(0, SelectMode.SelectError) || socket.Poll(0, SelectMode.SelectRead);
            }
            catch { return true; }
        }

        static void StartBackgroundCleanup(Thread acceptThread, List<Thread> workers)
        {
            try
            {
                Thread cleanup = new Thread(new ThreadStart(delegate
                {
                    JoinEventually(acceptThread);
                    foreach (Thread worker in workers) JoinEventually(worker);
                }));
                cleanup.IsBackground = true;
                cleanup.Name = "MJPEG cleanup";
                cleanup.Start();
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(DateTime.Now.ToString("s") + " MJPEG cleanup: " + ex); }
        }

        static void JoinEventually(Thread thread)
        {
            if (thread == null || thread == Thread.CurrentThread) return;
            try { thread.Join(); } catch (ThreadStateException) { }
        }
    }
}
