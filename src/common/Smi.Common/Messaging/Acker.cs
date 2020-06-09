﻿#nullable enable

using System;
using RabbitMQ.Client;

namespace Smi.Common.Messaging
{
    public class Acker
    {
        private readonly IModel? m;
        // Simple wrapper around a Rabbit Model, allowing only Acks and Nacks
        public Acker(IModel? _m)
        {
            if (_m !=null && _m.IsClosed)
                throw new ArgumentException("Model is closed");

            m = _m;
        }

        public void BasicAck(ulong n,bool multiple=false)
        {
            if (m != null)
            {
                lock (m)
                    m.BasicAck(n, multiple);
            }
        }

        public void BasicNack(ulong n,bool multiple=false,bool requeue=false)
        {
            if (m!=null)
            {
                lock (m)
                    m.BasicNack(n, multiple, requeue);
            }
        }

        // Only used in unit testing, in the SelfClosing test case.
        internal void Close()
        {
            m?.Close();
        }
    }
}