using System;
using System.Collections;
using System.Threading;

namespace GalaevNetwork3
{
    public delegate void PostToFirstWT(BitArray message);

    public delegate void PostToSecondWT(BitArray message);

    public static class Program
    {
        private static void Main(string[] args)
        {   
            ConsoleHelper.WriteToConsole("Главный поток", "");
            
            // Direction 1 (1->2)
            
            Semaphore firstReceiveSemaphoreDir1 = new Semaphore(0, 1);
            Semaphore secondReceiveSemaphoreDir1 = new Semaphore(0, 1);
            FirstThreadDir1 firstThreadDir1 = new FirstThreadDir1(ref secondReceiveSemaphoreDir1, ref firstReceiveSemaphoreDir1);
            SecondThreadDir1 secondThreadDir1 = new SecondThreadDir1(ref firstReceiveSemaphoreDir1, ref secondReceiveSemaphoreDir1);
            Thread threadFirstDir1 = new Thread(firstThreadDir1.FirstThreadMain);
            Thread threadSecondDir1 = new Thread(secondThreadDir1.SecondThreadMain);
            PostToFirstWT postToFirstWtDir1 = firstThreadDir1.ReceiveData;
            PostToSecondWT postToSecondWtDir1 = secondThreadDir1.ReceiveData;
            
            // Direction 2 (2->1)
            
            Semaphore firstReceiveSemaphoreDir2 = new Semaphore(0, 1);
            Semaphore secondReceiveSemaphoreDir2 = new Semaphore(0, 1);
            FirstThreadDir2 firstThreadDir2 = new FirstThreadDir2(ref secondReceiveSemaphoreDir2, ref firstReceiveSemaphoreDir2);
            SecondThreadDir2 secondThreadDir2 = new SecondThreadDir2(ref firstReceiveSemaphoreDir2, ref secondReceiveSemaphoreDir2);
            Thread threadFirstDir2 = new Thread(firstThreadDir2.FirstThreadMain);
            Thread threadSecondDir2 = new Thread(secondThreadDir2.SecondThreadMain);
            PostToFirstWT postToFirstWtDir2 = firstThreadDir2.ReceiveData;
            PostToSecondWT postToSecondWtDir2 = secondThreadDir2.ReceiveData;
            
            threadFirstDir1.Start(postToSecondWtDir1);
            threadSecondDir1.Start(postToFirstWtDir1);
            
            threadFirstDir2.Start(postToSecondWtDir2);
            threadSecondDir2.Start(postToFirstWtDir2);
            Console.ReadLine();
        }
    }
}