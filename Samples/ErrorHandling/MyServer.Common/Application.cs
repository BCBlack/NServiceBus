﻿using System;
using NServiceBus;

namespace MyServer.Common
{
    using System.Threading.Tasks;

    public class Application : IWantToRunAtStartup
    {
        public IBus Bus { get; set; }        

        public void Run()
        {            
            Console.WriteLine("Press 'S' to send a message that will throw an exception.");            
            Console.WriteLine("Press 'Q' to exit.");            
                        
            string cmd;

            while ((cmd = Console.ReadKey().Key.ToString().ToLower()) != "q")            
            {
                Console.WriteLine("");

                switch (cmd)
                {
                    case "s":
                        Console.Out.WriteLine("Sending...");
                        Parallel.For(0, 100, i =>
                            {
                                var m = new MyMessage {Id = Guid.NewGuid()};
                                Bus.SendLocal(m);
                            });
                        break;
                }                
            }
        }

        public void Stop()
        {            
        }
    }
}
