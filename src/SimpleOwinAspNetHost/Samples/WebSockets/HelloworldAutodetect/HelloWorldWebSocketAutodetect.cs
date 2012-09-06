﻿
namespace SimpleOwinAspNetHost.Samples.WebSockets.HelloworldAutodetect
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using SimpleOwinAspNetHost.Middlewares;

    using AppFunc = System.Func<System.Collections.Generic.IDictionary<string, object>, System.Threading.Tasks.Task>;

    using WebSocketSendAsync = System.Func<
                System.ArraySegment<byte>, // data
                int, // message type
                bool, // end of message
                System.Threading.CancellationToken, // cancel
                System.Threading.Tasks.Task>;

    using WebSocketReceiveResultTuple = System.Tuple<
                        int, // messageType
                        bool, // endOfMessage
                        int?, // count
                        int?, // closeStatus
                        string>; // closeStatusDescription

    using WebSocketReceiveAsync = System.Func<
                System.ArraySegment<byte> /* data */,
                System.Threading.CancellationToken /* cancel */,
                System.Threading.Tasks.Task<
                    System.Tuple<
                        int, // messageType
                        bool, // endOfMessage
                        int?, // count
                        int?, // closeStatus
                        string>>>; // closeStatusDescription

    using WebSocketCloseAsync = System.Func<
                int, // closeStatus
                string, // closeDescription
                System.Threading.CancellationToken, // cancel
                System.Threading.Tasks.Task>;

#pragma warning disable 811
    using WebSocketAction = System.Func<
            System.Func< // WebSocketSendAsync 
                System.ArraySegment<byte>, // data
                int, // message type
                bool, // end of message
                System.Threading.CancellationToken, // cancel
                System.Threading.Tasks.Task>,
            System.Func< // WebSocketReceiveAsync
                System.ArraySegment<byte> /* data */,
                System.Threading.CancellationToken /* cancel */,
                System.Threading.Tasks.Task<
                    System.Tuple<
                        int, // messageType
                        bool, // endOfMessage
                        int?, // count
                        int?, // closeStatus
                        string>>>, // closeStatusDescription
             System.Func< // WebSocketCloseAsync
                int, // closeStatus
                string, // closeDescription
                System.Threading.CancellationToken, // cancel
                System.Threading.Tasks.Task>,
            System.Threading.Tasks.Task>; // complete
#pragma warning restore 811

    public class HelloWorldWebSocketAutodetect
    {
        private static readonly Task CachedCompletedResultTupleTask;

        static HelloWorldWebSocketAutodetect()
        {
            var tcs = new TaskCompletionSource<int>();
            tcs.TrySetResult(0);
            CachedCompletedResultTupleTask = tcs.Task;
        }

        private static Func<AppFunc, AppFunc> MainHelloworldSocketApp()
        {
            return app => env =>
            {
                var responseBody = (Stream)env["owin.ResponseBody"];

                var webSocketSupport = Get<string>(env, "websocket.Support");
                if (webSocketSupport != null && webSocketSupport.Contains("WebSocketFunc"))
                {
                    // websocket supported
                    env["owin.ResponseStatusCode"] = 101;
                    WebSocketAction webSocketBody = async (sendAsync, receiveAsync, closeAsync) =>
                    {
                        // note: make sure to catch errors when calling sendAsync, receiveAsync and closeAsync
                        // for simiplicity this code does not handle errors
                        var buffer = new ArraySegment<byte>(new byte[6]);
                        while (true)
                        {
                            var webSocketResultTuple = await receiveAsync(buffer, CancellationToken.None);
                            int wsMessageType = webSocketResultTuple.Item1;
                            bool wsEndOfMessge = webSocketResultTuple.Item2;
                            int? count = webSocketResultTuple.Item3;
                            int? closeStatus = webSocketResultTuple.Item4;
                            string closeStatusDescription = webSocketResultTuple.Item5;

                            Debug.Write(Encoding.UTF8.GetString(buffer.Array, 0, count.Value));

                            if (wsEndOfMessge)
                                break;
                        }

                        await closeAsync((int)WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    };

                    env["websocket.Func"] = webSocketBody;
                }
                else
                {
                    env["owin.ResponseStatusCode"] = 200;
                    // do the actual copying to response stream here
                    var message = Encoding.UTF8.GetBytes("hello from owin");
                    responseBody.Write(message, 0, message.Length);
                }

                return app(env);
            };
        }

        public static IEnumerable<Func<AppFunc, AppFunc>> OwinApps()
        {
            var app = new List<Func<AppFunc, AppFunc>>();
            app.Add(AspNetWebSocketMiddleware.Middleware());
            app.Add(MainHelloworldSocketApp());
            return app;
        }

        public static AppFunc OwinApp()
        {
            var apps = OwinApps().ToList();
            // return SimpleOwinAspNetHandler.ConvertApp(apps);

            return
                env =>
                {
                    AppFunc next = null;
                    int index = 0;

                    next = env2 =>
                    {
                        if (index == apps.Count)
                            return CachedCompletedResultTupleTask; // we are done

                        Func<AppFunc, AppFunc> other = apps[index++];
                        return other(env3 => next(env3))(env2);
                    };

                    return next(env);
                };
        }

        private static T Get<T>(IDictionary<string, object> env, string key)
        {
            object value;
            return env.TryGetValue(key, out value) && value is T ? (T)value : default(T);
        }
    }
}