using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

class Server
{
    /// <summary>
    /// Страницы
    /// </summary>
    public Dictionary<string, byte[]> Pages = new Dictionary<string, byte[]>();

    TcpListener Listener; // Объект, принимающий TCP-клиентов

    // Запуск сервера
    public Server(int Port)
    {

        Listener = new TcpListener(IPAddress.Any, Port); // Создаем "слушателя" для указанного порта
        Listener.Start(); // Запускаем его

        // В бесконечном цикле
        while (true)
        {
            // Принимаем новых клиентов. После того, как клиент был принят, он передается в новый поток (ClientThread)
            // с использованием пула потоков.
            ThreadPool.QueueUserWorkItem(new WaitCallback(ClientThread), Listener.AcceptTcpClient());
        }
    }

    static void ClientThread(object StateInfo)
    {
        HtClient cli = new HtClient()
        {
            Client = (TcpClient)StateInfo,
            buffer = new byte[1024],
        };

        // Объявим строку, в которой будет хранится запрос клиента
        string Request = "";
        // Буфер для хранения принятых от клиента данных
        // Переменная для хранения количества байт, принятых от клиента
        int Count;
        // Читаем из потока клиента до тех пор, пока от него поступают данные
        while ((Count = cli.Client.GetStream().Read(cli.buffer, 0, cli.buffer.Length)) > 0) //======================================ПЕРЕДЕЛАТЬ ПОИСК ЗАГОЛОВКОВ!!!!===============================
        {
            // Преобразуем эти данные в строку и добавим ее к переменной Request
            Request += Encoding.ASCII.GetString(cli.buffer, 0, Count);
            // Запрос должен обрываться последовательностью \r\n\r\n
            // Либо обрываем прием данных сами, если длина строки Request превышает 4 килобайта
            // Нам не нужно получать данные из POST-запроса (и т. п.), а обычный запрос
            // по идее не должен быть больше 4 килобайт
            if (Request.IndexOf("\r\n\r\n") >= 0 || Request.Length > 4096)
            {
                break;
            }
        }

        // Парсим строку запроса с использованием регулярных выражений
        // При этом отсекаем все переменные GET-запроса
        Match ReqMatch = Regex.Match(Request, @"^(\w+)\s+([^\s\?]+)[^\s]*\s+HTTP\/.*|");

        // Если запрос не удался
        if (ReqMatch == Match.Empty)
        {
            SendError(cli.Client, 400);
            return;
        }
        string RequestUri = ReqMatch.Groups[1].Value;
        RequestUri = Uri.UnescapeDataString(RequestUri);
        if (RequestUri.IndexOf("..") >= 0)
        {
            SendError(Client, 400);
            return;
        }

        switch (ReqMatch.Groups[1].Value)
        {
            case "GET":

                switch (RequestUri)
                {
                    case "state":
                        break;
                    case "map":
                        break;
                    default:
                        SendError(Client, 400);
                        break;
                }

                string FilePath = "www/" + RequestUri;

                // Если в папке www не существует данного файла, посылаем ошибку 404
                if (!File.Exists(FilePath))
                {
                    SendError(Client, 404);
                    return;
                }

                // Получаем расширение файла из строки запроса
                string Extension = RequestUri.Substring(RequestUri.LastIndexOf('.'));

                // Тип содержимого
                string ContentType = "";

                // Пытаемся определить тип содержимого по расширению файла
                switch (Extension)
                {
                    case ".htm":
                    case ".html":
                        ContentType = "text/html";
                        break;
                    case ".css":
                        ContentType = "text/stylesheet";
                        break;
                    case ".js":
                        ContentType = "text/javascript";
                        break;
                    case ".jpg":
                        ContentType = "image/jpeg";
                        break;
                    case ".jpeg":
                    case ".png":
                    case ".gif":
                        ContentType = "image/" + Extension.Substring(1);
                        break;
                    default:
                        if (Extension.Length > 1)
                        {
                            ContentType = "application/" + Extension.Substring(1);
                        }
                        else
                        {
                            ContentType = "application/unknown";
                        }
                        break;
                }
                break;
            case "POST":
                SendError(Client, 501);
                break;


        }



        Client.Close();
    }

    private static void sendFile(TcpClient Client, string FilePath, byte[] buffer)
    {
        int Count;
        // Открываем файл, страхуясь на случай ошибки
        FileStream FS;
        try
        {
            FS = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception)
        {
            // Если случилась ошибка, посылаем клиенту ошибку 500
            SendError(Client, 500);
            return;
        }

        // Посылаем заголовки
        string Headers = "HTTP/1.1 200 OK\nContent-Type: text/xml\nContent-Length: " + FS.Length + "\n\n";
        byte[] HeadersBuffer = Encoding.ASCII.GetBytes(Headers);
        Client.GetStream().Write(HeadersBuffer, 0, HeadersBuffer.Length);

        // Пока не достигнут конец файла
        while (FS.Position < FS.Length)
        {
            // Читаем данные из файла
            Count = FS.Read(Buffer, 0, Buffer.Length);
            // И передаем их клиенту
            Client.GetStream().Write(Buffer, 0, Count);
        }

        // Закроем файл и соединение
        FS.Close();
    }


    // Остановка сервера
    ~Server()
    {
        // Если "слушатель" был создан
        if (Listener != null)
        {
            // Остановим его
            Listener.Stop();
        }
    }

    // Отправка страницы с ошибкой
    private static void SendError(TcpClient Client, int Code)
    {
        // Получаем строку вида "200 OK"
        // HttpStatusCode хранит в себе все статус-коды HTTP/1.1
        string CodeStr = Code.ToString() + " " + ((HttpStatusCode)Code).ToString();
        // Код простой HTML-странички
        string Html = "<html><body><h1>" + CodeStr + "</h1></body></html>";
        // Необходимые заголовки: ответ сервера, тип и длина содержимого. После двух пустых строк - само содержимое
        string Str = "HTTP/1.1 " + CodeStr + "\nContent-type: text/html\nContent-Length:" + Html.Length.ToString() + "\n\n" + Html;
        // Приведем строку к виду массива байт
        byte[] Buffer = Encoding.ASCII.GetBytes(Str);
        // Отправим его клиенту
        Client.GetStream().Write(Buffer, 0, Buffer.Length);
        // Закроем соединение
        Client.Close();
    }
    public enum ReqType : byte
    {
        GET, SET
    }

    public class HtClient
    {
        public readonly ReqType Type;
        public TcpClient Client;
        public byte[] buffer;
        public int Count = 0;
        private readonly NetworkStream Stream;

        public HtClient(TcpClient client, int BufferSize)
        {
            Client = client;
            buffer = new byte[BufferSize];
            Stream = client.GetStream();
            string tmp = string.Empty;
            //Получаем тип запроса 
            int code = 0;
            while (code != -1)
            {
                code =
                tmp +=
            }

            foreach (byte b in Stream)
        }

        private string ReadHeaderString()
        {
            string Output = string.Empty;
            int code;
            byte[] output = new byte[1];
            while (true)
            {
                code = Stream.ReadByte();
                if (code == -1)
                    throw new EndOfStreamException();
                output[0] = (byte)code;
                Output += Encoding.ASCII.GetString(output)

            }

        }
    }
}
