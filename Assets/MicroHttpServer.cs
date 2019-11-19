using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace UpHash
{
    public class MicroHttpServer
    {
        public class Response
        {
            public string contentType = "text/plain";
            public byte[] value = new byte[0];
        }
        public delegate Response RequestedCallback(string uri, string postText);

        public static IPAddress address { get { return Dns.GetHostAddresses(Dns.GetHostName()).FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork); } }
        public const string port = "8080";

        static object locker = new object();
        static HttpListener listenrInstance;
        static Dictionary<string, RequestedCallback> callbacks = new Dictionary<string, RequestedCallback>();
        static Thread worker;


        /// <summary>
        /// サーバーをバッググラウンドスレッドで実行します。
        /// </summary>
        [RuntimeInitializeOnLoadMethod]
        public static void Start()
        {
            worker = new Thread(new ThreadStart(Init));
            worker.Start();
        }

        /// <summary>
        /// サーバーを強制的に終了します。
        /// </summary>
        public static void Kill()
        {
            if(worker != null && worker.IsAlive)
            {
                worker.Abort();
            }
        }

        static void Init()
        {
            lock(locker)
            {
                listenrInstance = new HttpListener();
                foreach (string prefix in callbacks.Keys)
                {
                    listenrInstance.Prefixes.Add(prefix);
                }
                listenrInstance.Start();

                AddFunction("", (_0, post) => new Response() { value = Encoding.UTF8.GetBytes(post) }); // Debug
                Debug.Log(string.Format("MicroHttpServer started! {0}:{1}", address, port));
            }

            while(true)
            {
                listenrInstance.EndGetContext(listenrInstance.BeginGetContext(OnRequested, null));
            }
        }

        static void OnRequested(IAsyncResult result)
        {
            lock(locker)
            {
                try
                {
                    System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
                    watch.Start();
                    HttpListenerContext context = listenrInstance.EndGetContext(result);

                    try
                    {
                        string client = context.Request.RemoteEndPoint.ToString();
                        string uri = context.Request.Url.ToString();
                        string post = null;
                        if(context.Request.HttpMethod.Contains("POST"))
                        {
                            using(StreamReader sr = new StreamReader(context.Request.InputStream))
                            {
                                post = sr.ReadToEnd();
                            }
                        }

                        Regex pattern = new Regex("http://[^/]+/([^/?#]+)");
                        string path = pattern.Match(uri).Groups[1].Value;
                        Response response = callbacks[path](uri, post ?? "empty");

                        context.Response.StatusCode = 200; // OK
                        context.Response.ContentType = response.contentType;
                        context.Response.AddHeader("ProcessSeconds", watch.Elapsed.TotalMilliseconds.ToString());
                        context.Response.Close(response.value, false);

                        Debug.Log(string.Format("MicroHttpServer Requested {0} from {1}\n{2}", uri, client, post));
                    }
                    catch(Exception e0)
                    {
                        context.Response.StatusCode = 500; // Server internal error
                        context.Response.ContentType = "text/plain";
                        context.Response.AddHeader("MicroHttpServer ProcessSeconds", watch.Elapsed.TotalMilliseconds.ToString());
                        context.Response.Close(Encoding.UTF8.GetBytes(e0.ToString()), false);
                        throw e0;
                    }
                }
                catch(Exception e1)
                {
                    Debug.LogError(e1);
                }
            }
        }

        /// <summary>
        /// サーバーの機能を追加します。
        /// </summary>
        /// <param name="path">スキーム、ドメイン、/は不要</param>
        public static void AddFunction(string path, RequestedCallback callback)
        {
            lock(locker)
            {
                callbacks[path] = callback;
                if(listenrInstance != null && !listenrInstance.Prefixes.Contains(path))
                {
                    string prefix = string.Format("http://{0}:{1}/{2}", address, port, path).TrimEnd('/') + "/";
                    listenrInstance.Prefixes.Add(prefix);
                    string localPrefix = string.Format("http://localhost:{0}/{1}", port, path).TrimEnd('/') + "/";
                    listenrInstance.Prefixes.Add(localPrefix);
                }
            }
        }

        #if UNITY_EDITOR
        // [UnityEditor.MenuItem("MicroHttpServer/Test")]
        static void Test()
        {
            UnityWebRequest req = new UnityWebRequest("http://localhost:8080", "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("Hoge"));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SendWebRequest().completed += _ => Debug.Log(req.downloadHandler.text);
        }
        #endif
    }
}