using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Smartalock.API;


public delegate void SLEventReceived(SLEvent e);
public delegate void SLDisconnected();
public delegate void SLDebug(string message);

public class SLWebsocket
{
    public const int CONNECT_TIMEOUT = 5000;

    private ClientWebSocket? ws;
    private CancellationTokenSource cancelTokenSource;
    private string hostname;
    private int port;
    private string username;
    private string password;

    private SemaphoreSlim connectSemaphore = new SemaphoreSlim(1, 1);

    private bool connected;
    private bool authenticated;

    public event SLEventReceived? OnEventReceived;
    public event SLDisconnected? OnDisconnected;
    public event SLDebug? OnDebug;

    private BlockingCollection<SLRequest> sendQueue = new BlockingCollection<SLRequest>();
    private BlockingCollection<SLRequest> sentQueue = new BlockingCollection<SLRequest>();

    public SLWebsocket(string hostname, int port, string username, string password)
    {
        this.ws = null;
        this.hostname = hostname;
        this.port = port;
        this.username = username;
        this.password = password;
        this.connected = false;
        this.authenticated = false;
        this.cancelTokenSource = new CancellationTokenSource();
    }

    private JsonObject GetAuthData(JsonNode? nonceInfo)
    {
        JsonObject authData = new JsonObject
        {
            ["user"] = username
        };
        if (this.password.Length > 0)
        {
            string s = "";
            if (nonceInfo != null)
            {
                JsonObject obj = nonceInfo.AsObject();
                if (obj.ContainsKey("secret"))
                {
                    s = (string)obj["secret"];
                }
            }
            s += this.password;
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(s);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("x2"));
                }
                authData.Add("auth", sb.ToString());
            }

        }
        return authData;
    }

    public async Task<SLResponse> Connect()
    {
        SLResponse result;
        await connectSemaphore.WaitAsync();
        try
        {
            if (connected)
            {
                await DisconnectImpl();
            }

            this.cancelTokenSource.Dispose();
            this.cancelTokenSource = new CancellationTokenSource();

            ws = new ClientWebSocket();
            string websocketURI = "ws://" + hostname + ":" + port;

            CancellationTokenSource connectTimeout = new CancellationTokenSource();
            connectTimeout.CancelAfter(CONNECT_TIMEOUT);


            try
            {
                await ws.ConnectAsync(new Uri(websocketURI), connectTimeout.Token);


                if (ws.State == WebSocketState.Open)
                {
                    this.connected = true;
                    this.authenticated = false;

                    Task transmitTask = Task.Run(() => TransmitTask(ws));
                    Task receiveTask = Task.Run(() => ReceiveTask(ws));

                    SLResponse nonceResponse = await RequestImpl(SLMethod.GET, "/secret", null);
                    if (nonceResponse.Result)
                    {
                        JsonObject authData = GetAuthData(nonceResponse.Info);
                        SLResponse authResponse = await RequestImpl(SLMethod.POST, "/auth", authData);
                        result = authResponse;
                        if (authResponse.Result)
                        {
                            this.authenticated = true;
                        }
                    }
                    else
                    {
                        // No nonce was received - return an error
                        await DisconnectImpl();
                        result = SLResponse.Error(0, "Failed to get nonce", null);
                    }
                }
                else
                {
                    // We didn't connect - return an error
                    result = SLResponse.Error(0, "Failed to connect", null);
                }
            }
            catch (TaskCanceledException)
            {
                result = SLResponse.Error(0, "Connection timeout", null);
            }

            connectTimeout.Dispose();
        } catch (Exception)
        {
            result = SLResponse.Error(0, "Connection exception", null);

        } finally
        {
            connectSemaphore.Release();
        }
        return result;
    }

    public async Task Disconnect()
    {
        await connectSemaphore.WaitAsync();
        try
        {
            await DisconnectImpl();
        }
        catch (Exception e)
        {
            OnDebug?.Invoke(e.Message);
        }
        finally {
            connectSemaphore.Release();
        }
    }

    private async Task DisconnectImpl()
    {
        authenticated = false;
        if (connected)
        {
            connected = false;
            if (ws != null)
            {
                if (ws.State == WebSocketState.Open)
                {
                    CancellationTokenSource disconnectTimeout = new CancellationTokenSource();
                    disconnectTimeout.CancelAfter(5000);
                    try
                    {
                        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, disconnectTimeout.Token);
                    } catch (TaskCanceledException)
                    {
                        // Warn - failed to close websocket
                    }
                    disconnectTimeout.Dispose();
                }
                ws = null;
            }

            cancelTokenSource.Cancel();

            cancelTokenSource.Dispose();
            cancelTokenSource = new CancellationTokenSource();


            // TODO: Empty request queue and send an error
            // TODO: Empty the sentQueue
            // TODO: Empty the sendQueue

            OnDisconnected?.Invoke();
        }
    }

    private async Task TransmitTask(ClientWebSocket wsTX)
    {
        CancellationToken token = cancelTokenSource.Token;
        do
        {
            try
            {
                SLRequest request = sendQueue.Take(token);
                string sendString = request.FormatRequest();
                byte[] sendData = Encoding.UTF8.GetBytes(sendString);
                OnDebug?.Invoke("Sending: " + sendString);
                sentQueue.Add(request);
                await wsTX.SendAsync(sendData, WebSocketMessageType.Binary, true, token);
            } catch (TaskCanceledException)
            {
                // No next request as we were cancelled whilst waiting for one
            }
        } while (!token.IsCancellationRequested && wsTX.State == WebSocketState.Open);
        OnDebug?.Invoke("TransmitTask detected disconnect");
        if (!token.IsCancellationRequested)
        {
            await Disconnect();
        }
        OnDebug?.Invoke("Transmit task ended");
    }


    private SLResponse DecodeResponse(JsonObject obj)
    {
        SLResponse? response = null;
        try
        {
            bool result = false;
            int code = 0;
            string? message = null;
            JsonNode? info = null;

            if (obj.ContainsKey("result"))
            {
                result = (bool)obj["result"];
            }
            if (obj.ContainsKey("code"))
            {
                code = (int)obj["code"];
            }
            if (obj.ContainsKey("message"))
            {
                message = obj["message"].ToString();
            }
            if (obj.ContainsKey("info"))
            {
                info = obj["info"];
            }

            response = new SLResponse(result, code, message, info);
        }
        catch (Exception ex)
        {
            OnDebug?.Invoke("Exception! " + ex.ToString());
        }
        return response ?? SLResponse.Error(0, "Cannot parse response", null);
    }

    private SLEvent DecodeEvent(JsonObject obj)
    {
        SLEvent? response = null;
        try
        {
            int code = 0;
            string? message = null;
            JsonNode? info = null;

            if (obj.ContainsKey("code"))
            {
                code = (int)obj["code"];
            }
            if (obj.ContainsKey("message"))
            {
                message = obj["message"].ToString();
            }
            if (obj.ContainsKey("info"))
            {
                info = obj["info"];
            }

            response = new SLEvent(code, message, info);
        }
        catch (Exception ex)
        {
            OnDebug?.Invoke("Exception! " + ex.ToString());
        }
        return response ?? new SLEvent(0, "Cannot parse event", null);
    }

    private async Task ReceiveTask(ClientWebSocket wsRX)
    {
        CancellationToken token = cancelTokenSource.Token;

        bool invalidData = false;
        var buffer = WebSocket.CreateClientBuffer(8192, 8192);
        string lineBuffer = "";
        while (!invalidData && wsRX.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            try
            {
                WebSocketReceiveResult taskResult = await wsRX.ReceiveAsync(buffer, token);
                string recvString = Encoding.UTF8.GetString(buffer.Array, 0, taskResult.Count);
                OnDebug?.Invoke("recv=" + recvString);
                lineBuffer += recvString;
                int i = lineBuffer.IndexOf("\n");
                if (i >= 0)
                {
                    string line = lineBuffer.Substring(0, i);
                    lineBuffer = lineBuffer.Substring(i + 1);
                    // Convert to JSON
                    OnDebug?.Invoke("**** Processing line: " + line);
                    JsonObject? obj = null;
                    try
                    {
                        obj = JsonNode.Parse(line).AsObject();
                        if (obj.ContainsKey("type"))
                        {
                            string objType = obj["type"].ToString();
                            if (objType.Equals("response"))
                            {
                                OnDebug?.Invoke("result object");
                                SLResponse response = DecodeResponse(obj);
                                SLRequest req = sentQueue.Take(); // TODO: TryTake()? This should never fail
                                req.SetResponse(response);
                            }
                            else if (objType.Equals("event"))
                            {
                                OnDebug?.Invoke("event object");
                                SLEvent e = DecodeEvent(obj);
                                OnEventReceived?.Invoke(e);

                            }
                            else
                            {
                                OnDebug?.Invoke("unknown object: " + objType);
                            }

                        }
                        else
                        {
                            // Unknown data - terminate websocket
                            OnDebug?.Invoke("Invalid data received");
                            invalidData = true;
                        }
                    }
                    catch (JsonException jse)
                    {
                        invalidData = true;
                        OnDebug?.Invoke("Failed to parse response line");
                    }
                }
            } catch (TaskCanceledException)
            {
                // cancelled!
            }
        }
        if (!token.IsCancellationRequested)
        {
            OnDebug?.Invoke("ReceiveTask detected disconnect");
            await Disconnect();
        }
        OnDebug?.Invoke("ReceiveTask ended");
    }

    private async Task<SLResponse> RequestImpl(SLMethod method, string uri, JsonObject? data)
    {
        SLRequest request = new SLRequest(method, uri, data);
        OnDebug?.Invoke("Adding request to queue");
        sendQueue.Add(request);
        OnDebug?.Invoke("Waiting for response");
        SLResponse response = await request.WaitForResponse(cancelTokenSource.Token);
        OnDebug?.Invoke("Returning response");
        return response;
    }

    public async Task<SLResponse> Request(SLMethod method, string uri, JsonObject? data)
    {
        SLResponse result;
        if (connected)
        {
            if (authenticated)
            {
                result = await RequestImpl(method, uri, data);
            }
            else
            {
                result = SLResponse.Error(0, "Not authenticated", null);
            }
        }
        else
        {
            result = SLResponse.Error(0, "Not connected", null);
        }
        return result;
    }
}

class SLRequest
{
    SLMethod method;
    string uri;
    JsonObject? data;
    SLResponse? response;
    SemaphoreSlim semaphore = new SemaphoreSlim(0, 1);

    public SLRequest(SLMethod method, string uri, JsonObject? data)
    {
        this.method = method;
        this.uri = uri;
        this.data = data;
        this.response = null;
    }
    public void SetResponse(SLResponse response)
    {
        this.response = response;
        // notify any waiters that a response is ready
        semaphore.Release(1);
    }

    public async Task<SLResponse> WaitForResponse(CancellationToken token)
    {
        SLResponse result;
        try
        {
            await semaphore.WaitAsync(token);
        } catch (TaskCanceledException)
        {
            // Task cancelled
        }
        result = this.response ?? SLResponse.Error(0, "No response received", null);
        return result;
    }

    public string FormatRequest()
    {
        string result;
        if (method == SLMethod.GET)
        {
            result = "GET " + uri;
            int elementCount = 0;
            if (data != null)
            {
                IEnumerator<KeyValuePair<string, JsonNode?>> values = data.GetEnumerator();
                while (values.MoveNext())
                {
                    elementCount++;
                    KeyValuePair<string, JsonNode?> kv = values.Current;
                    string key = kv.Key;
                    JsonNode? value = kv.Value;

                    if (elementCount == 1)
                    {
                        result += "?";
                    }
                    else
                    {
                        result += "&";
                    }
                    result += Uri.EscapeDataString(key);
                    result += "=";
                    if (value != null)
                    {
                        result += value.ToString();
                    }
                }
            }
            result += "\n";

        }
        else if (method == SLMethod.POST)
        {
            result = "POST " + uri + "\n";
            if (data != null)
            {
                JsonSerializerOptions opts = new JsonSerializerOptions(JsonSerializerOptions.Default);
                opts.WriteIndented = false;
                result += data.ToJsonString(opts);
            }
            else
            {
                result += "{}";
            }
            result += "\n";
        }
        else
        {
            // Shouldn't get here!
            result = "";
        }
        return result;
    }
}