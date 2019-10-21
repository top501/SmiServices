﻿
using System;

namespace Microservices.Common.Execution
{
    /// <summary>
    /// Wraps construction and startup of your applications MicroserviceHost. Handles Exceptions thrown during construction / setup as well as Ctrl+C support in standardised way
    /// </summary>
    public class MicroserviceHostBootstrapper
    {
        private readonly Func<MicroserviceHost> _func;


        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="func">Construct with your host constructor call and then 'return bootStrapper.Main();'</param>
        public MicroserviceHostBootstrapper(Func<MicroserviceHost> func)
        {
            _func = func;
        }

        public int Main()
        {
            Console.WriteLine("Bootstrapper -> Main called, constructing host");

            MicroserviceHost host;

            try
            {
                host = _func();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Failed to construct host:\n" + e);
                return -1;
            }

            Console.WriteLine("Bootstrapper -> Host constructed, starting aux connections");

            try
            {
                Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
                {
                    e.Cancel = true;
                    host.Stop("Ctrl+C pressed");
                };

                try
                {
                    host.StartAuxConnections();
                    Console.WriteLine("Bootstrapper -> Host aux connections started, calling Start()");

                    host.Start();
                    Console.WriteLine("Bootstrapper -> Host started");
                }
                catch (Exception e)
                {
                    host.Fatal("Failed to start host", e);
                    return -2;
                }

                Console.WriteLine("Bootstrapper -> Exiting main");
            }
            finally
            {
                var disposable = host as IDisposable;

                if (disposable != null)
                    disposable.Dispose();
            }

            // Only thing keeping process from exiting after this point are any
            // running tasks (i.e. RabbitMQ consumer tasks)
            return 0;
        }
    }
}