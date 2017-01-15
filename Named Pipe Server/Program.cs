using System;
using System.IO;
using System.IO.Pipes;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Named_Pipe_Server
{
    class Program
    {
        static string LogDirectory = "H:\\Logs\\Pipe";

        static NamedPipeServerStream srv;
        
        static FileStream fs;
        static TextWriter tw;

        static volatile bool on = false;
        static char[] charBuffer = new char[1];

        static int mode = 0;
        static string PipeName = null;

        static BlockingCollection<ArraySegment<byte>> Buffers = new BlockingCollection<ArraySegment<byte>>(1000);
        static Thread Dumper = new Thread(DumpBuffers);

        static void Main(string[] args)
        {
            Console.Title = "Named Pipe Server";

            if (!Console.IsInputRedirected)
            {
                    Console.Write("Choose mode: [A]scii or [B]inary? (Escape to quit) ");

            reask:
                var _c = Console.ReadKey(true);

                switch (_c.Key)
                {
                case ConsoleKey.A:
                    mode = 0;
                    PipeName = "AsciiDump";

                    Console.Title = "Ascii Dump | Named Pipe Server";
                    break;

                case ConsoleKey.B:
                    mode = 1;
                    PipeName = "BytesDump";

                    Console.Title = "Bytes Dump | Named Pipe Server";
                    break;

                case ConsoleKey.Escape:
                    return;

                default:
                    goto reask;
                }
            }
            else
            {
                if (args.Length != 1 || !"ab".Contains(args[0].ToLower()))
                {
                    Console.Error.WriteLine("Please provide one argument: A or B.");

                    return;
                }

                if (args[0].ToLower() == "a")
                {
                    mode = 0;
                    PipeName = "AsciiDump";

                    Console.Title = "Ascii Dump | Named Pipe Server";
                }
                else
                {
                    mode = 1;
                    PipeName = "BytesDump";

                    Console.Title = "Bytes Dump | Named Pipe Server";
                }
            }

            //  Now we do da shiz.

            ConstructPipe();

            Dumper.Start();

            if (!Console.IsInputRedirected)
                while (on)
                {
                    var c = Console.ReadKey(true);

                    charBuffer[0] = c.KeyChar;

                    if (c.Key == ConsoleKey.Escape)
                        on = false;
                    else if (c.Key == ConsoleKey.Delete)
                    {
                        Console.Clear();
                        Console.SetCursorPosition(0, 0);

                        bytesLineCursor = bytesColumnCursor = 0;

                        withColor("SCREEN CLEARED", writeln, ConsoleColor.Green, ConsoleColor.DarkGray);
                    }
                    else if (c.Key == ConsoleKey.PageDown)
                    {
                        int newTop = 0;

                        if (c.Modifiers.HasFlag(ConsoleModifiers.Shift))
                            newTop = Console.WindowTop + Console.WindowHeight / 2;
                        else if (Console.WindowHeight >= 8)
                            newTop = Console.WindowTop + Console.WindowHeight - 3;
                        else if (Console.WindowHeight >= 6)
                            newTop = Console.WindowTop + Console.WindowHeight - 2;
                        else
                            newTop = Console.WindowTop + Console.WindowHeight;

                        if (newTop > Console.CursorTop - Console.WindowHeight + 1)
                            newTop = Console.CursorTop - Console.WindowHeight + 1;

                        Console.WindowTop = newTop;
                    }
                    else if (c.Key == ConsoleKey.PageUp)
                    {
                        int newTop = 0;

                        if (c.Modifiers.HasFlag(ConsoleModifiers.Shift))
                            newTop = Console.WindowTop - Console.WindowHeight / 2;
                        else if (Console.WindowHeight >= 8)
                            newTop = Console.WindowTop - Console.WindowHeight + 3;
                        else if (Console.WindowHeight >= 6)
                            newTop = Console.WindowTop - Console.WindowHeight + 2;
                        else
                            newTop = Console.WindowTop - Console.WindowHeight;

                        if (newTop < 0)
                            newTop = 0;

                        Console.WindowTop = newTop;
                    }
                    else if (c.Key == ConsoleKey.DownArrow)
                    {
                        int newTop = Console.WindowTop + 1;

                        if (newTop > Console.CursorTop - Console.WindowHeight + 1)
                            newTop = Console.CursorTop - Console.WindowHeight + 1;

                        Console.WindowTop = newTop;
                    }
                    else if (c.Key == ConsoleKey.UpArrow)
                    {
                        int newTop = Console.WindowTop - 1;

                        if (newTop < 0)
                            newTop = 0;

                        Console.WindowTop = newTop;
                    }
                    else if (srv != null && srv.IsConnected)
                    {
                        var bytes = Encoding.UTF8.GetBytes(charBuffer, 0, 1);

                        srv.Write(bytes, 0, bytes.Length);
                        
                        srv.Flush();
                    }
                }
            else
                while (on)
                {
                    int c = Console.Read();

                    charBuffer[0] = Convert.ToChar(c);

                    if (srv != null && srv.IsConnected)
                    {
                        var bytes = Encoding.UTF8.GetBytes(charBuffer, 0, 1);

                        srv.Write(bytes, 0, bytes.Length);

                        srv.Flush();
                    }
                }

            DestructPipe();

            Dumper.Join(1000);

            if (Dumper.IsAlive)
                Dumper.Abort();
        }

        static void DumpBuffers()
        {
            char[] bufa = new char[1 << 16];

            while (on || Buffers.Count > 0)
            {
                var seg = Buffers.Take();
                
                if (mode == 0)
                {
                    var str = Encoding.UTF8.GetString(seg.Array, seg.Offset, seg.Count);

                    Console.Write(str);
                    tw.Write(str);
                }
                else if (mode == 1)
                {
                    writeBytes(seg.Array, seg.Count);
                    fs.Write(seg.Array, seg.Offset, seg.Count);
                }
            }
        }

        static void DestructPipe()
        {
            if (srv != null)
            {
                if (srv.IsConnected)
                    srv.Disconnect();

                srv.Dispose();
            }
        }

        static void ConstructPipe()
        {
            DestructPipe();

            srv = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            on = true;

            srv.BeginWaitForConnection(HandleClient, null);

            withColor("LISTENING", writeln, ConsoleColor.Green, ConsoleColor.DarkGray);
        }
        
        static void HandleClient(IAsyncResult ar)
        {
            try
            {
                srv.EndWaitForConnection(ar);
            }
            catch (ObjectDisposedException)
            {
                //  GG Microsoft.

                return;
            }

            if (srv.IsConnected)
            {
                withColor("CONNECTED", writeln, ConsoleColor.Blue, ConsoleColor.White);

                bytesLineCursor = bytesColumnCursor = 0;
                
                fs = File.OpenWrite(Path.Combine(LogDirectory, DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss\\.") + mode + ".txt"));
                tw = new StreamWriter(fs, Encoding.UTF8);

                byte[] bufb = new byte[1 << 16];
                int read = -1;

                while ((read = srv.Read(bufb, 0, bufb.Length)) > 0)
                {
                    Buffers.Add(new ArraySegment<byte>(bufb, 0, read));

                    bufb = new byte[1 << 16];
                }

                while (Buffers.Count > 0) Thread.Yield();
                //  Must not trash tw and fs until the dumper is finished.

                tw.Dispose();
                fs.Dispose();

                withColor("DISCONNECTED", writeln, ConsoleColor.Red, ConsoleColor.White);
            }

            if (on)
            {
                ConstructPipe();
            }
        }

        static void writeln(string msg)
        {
            if (Console.IsOutputRedirected)
            {
                Console.WriteLine();
                Console.WriteLine(msg);
            }
            else
            {
                if (Console.CursorLeft > 0)
                    Console.WriteLine();

                Console.Write(msg);

                Console.Write(new string(' ', Console.BufferWidth - Console.CursorLeft));
            }
        }

        static void withColor(string msg, Action<string> act, ConsoleColor fg, ConsoleColor bg)
        {
            if (Console.IsOutputRedirected)
                act.Invoke(msg);
            else
            {
                ConsoleColor oldFg = Console.ForegroundColor, oldBg = Console.BackgroundColor;

                Console.ForegroundColor = fg;
                Console.BackgroundColor = bg;

                act.Invoke(msg);

                Console.ForegroundColor = oldFg;
                Console.BackgroundColor = oldBg;
            }
        }

        static int bytesLineSize = 32;

        static long bytesLineCursor = 0;
        static int bytesColumnCursor = 0;

        static void writeBytes(byte[] buf, int cnt)
        {
            int done = 0;

            do
            {
                done += writeBytesLine(buf, done, cnt);
            } while (done < cnt);
        }

        static int writeBytesLine(byte[] buf, int off, int cnt)
        {
            if (bytesColumnCursor == 0)
                withColor(bytesLineCursor.ToString("X16"), Console.Write, ConsoleColor.Green, ConsoleColor.Black);

            var ret = Math.Min(bytesLineSize - bytesColumnCursor, cnt - off);

            for (int i = 0; i < ret; i++)
            {
                var j = bytesColumnCursor + i;

                if (j % 2 == 0)
                {
                    Console.Write(' ');

                    if (j % 8 == 0)
                        Console.Write(' ');
                }

                var str = buf[off + i].ToString("X2");

                Console.Write(new char[2] { str[1], str[0] });
            }

            bytesColumnCursor += ret;

            if (bytesColumnCursor == bytesLineSize)
            {
                bytesLineCursor += bytesLineSize;
                bytesColumnCursor = 0;

                Console.WriteLine();
            }

            return ret;
        }
    }
}
