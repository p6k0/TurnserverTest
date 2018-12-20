using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TurnserverTest
{
    class Program
    {
        static void Main(string[] args)
        {
            ThreadPool.SetMaxThreads(8, 8);
            // Установим минимальное количество рабочих потоков
            ThreadPool.SetMinThreads(2, 2);
            new Server(8080);
        }
    }
}
