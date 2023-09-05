using IgisBot;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.IO;
using System.Net;
using System.Diagnostics;
using Newtonsoft.Json;
using User = IgisBot.User;
using System.Text.RegularExpressions;

namespace TelegramBotExperiments
{
    class Program
    {
        static SqlConnection sqlConnection = new SqlConnection(@"Data Source = (LocalDB)\MSSQLLocalDB; AttachDbFilename=E:\IgisBot2\IgisBot\IgisBot\IgisDB.mdf;Integrated Security = True");
        static ITelegramBotClient bot = new TelegramBotClient("5643524599:AAFqVWsbQiWYKFyQP0euiNutyj5eozV7C2I");
        static List<User> Users;
        static DateTime dateOfRun = DateTime.Now.AddHours(-4);

        static int Minimum(int a, int b, int c) => (a = a < b ? a : b) < c ? a : c;

        static int LevenshteinDistance(string firstWord, string secondWord)
        {
            var n = firstWord.Length + 1;
            var m = secondWord.Length + 1;
            var matrixD = new int[n, m];

            const int deletionCost = 1;
            const int insertionCost = 1;

            for (var i = 0; i < n; i++)
            {
                matrixD[i, 0] = i;
            }

            for (var j = 0; j < m; j++)
            {
                matrixD[0, j] = j;
            }

            for (var i = 1; i < n; i++)
            {
                for (var j = 1; j < m; j++)
                {
                    var substitutionCost = firstWord[i - 1] == secondWord[j - 1] ? 0 : 1;

                    matrixD[i, j] = Minimum(matrixD[i - 1, j] + deletionCost,          // удаление
                                            matrixD[i, j - 1] + insertionCost,         // вставка
                                            matrixD[i - 1, j - 1] + substitutionCost); // замена
                }
            }

            return matrixD[n - 1, m - 1];
        }

        public static List<Stop> FindNearestStops(double x, double y, long userId)
        {
            var earthRadiusMeters = 6371000;
            List<Stop> stops = new List<Stop>();
            var allStops = GetAllStops(userId);
            foreach (var stop in allStops)
            {
                var dLat = degreesToRadians(stop.Latitude - x);
                var dLon = degreesToRadians(stop.Longitude - y);
                var lat1 = degreesToRadians(stop.Latitude);
                var lat2 = degreesToRadians(x);

                var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2) * Math.Cos(lat1) * Math.Cos(lat2);
                var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
                var newDistance = earthRadiusMeters * c;
                
                if(newDistance < 500)
                    stops.Add(stop);
            }
            return stops;
        }

        public static double degreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180;
        }

        public static ObservableCollection<Stop> GetAllStops(long userId)
        {
            var type = "";
            if (userId != null)
            {
                type = "WHERE Тип = ";
                type += Users.Where(i => i.Id == userId).First().Type == "tram" ? "N'Трамвай'" : "N'Автобус, Троллейбус'";
            }
            sqlConnection.Open();
            var sqlCommand = new SqlCommand($"SELECT * FROM Остановка {type}", sqlConnection);
            ObservableCollection<Stop> result = new ObservableCollection<Stop>();
            var reader = sqlCommand.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new Stop()
                {
                    Id = (int)reader["Id"],
                    FullName = reader["Полное название"].ToString(),
                    ShortName = reader["Укороченное название"].ToString(),
                    Direction = reader["Направление движения"].ToString(),
                    Type = reader["Тип"].ToString(),
                    Latitude = (double)reader["Широта"],
                    Longitude = (double)reader["долгота"],
                    Obligation = (int)reader["Обязательность"],
                });
            }
            reader.Close();
            sqlConnection.Close();
            return result;
        }

        public static Stop GetStopById(int stopNumber)
        {
            sqlConnection.Open();
            var sqlCommand = new SqlCommand($"SELECT * FROM Остановка WHERE Id = {stopNumber}", sqlConnection);
            Stop stop = null;
            var reader = sqlCommand.ExecuteReader();
            while (reader.Read())
            {
                stop = new Stop()
                {
                    Id = (int)reader["Id"],
                    FullName = reader["Полное название"].ToString(),
                    ShortName = reader["Укороченное название"].ToString(),
                    Direction = reader["Направление движения"].ToString(),
                    Type = reader["Тип"].ToString(),
                    Latitude = (double)reader["Широта"],
                    Longitude = (double)reader["долгота"],
                    Obligation = (int)reader["Обязательность"],
                };
            }
            reader.Close();
            sqlConnection.Close();
            return stop;
        }

        public static Tuple<Stop, int, int> FindNearestStopRoute(List<Stop> stops, double uLat, double uLong)
        {
            int minDistance = 9999;
            int minTime = 9999;
            Stop nearestStop = null;
            foreach (var stop in stops)
            {
                var distanceInfo = GetDistanceInfo(stop.Latitude, stop.Longitude, uLat, uLong);
                if (distanceInfo.Item2 < minDistance)
                {
                    minDistance = distanceInfo.Item2;
                    minTime = (int)Math.Floor((double)(distanceInfo.Item1) / 60);
                    nearestStop = stop;
                }
            }
            return new Tuple<Stop, int, int>(nearestStop, minTime, minDistance);
        }

        public static Tuple<int, int> GetDistanceInfo(double lat1, double lon1, double lat2, double lon2)
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            string slat1 = lat1.ToString();
            string slon1 = lon1.ToString();
            string slat2 = lat2.ToString();
            string slon2 = lon2.ToString();
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://router.hereapi.com/v8/routes" +
                $"?origin={slat1},{slon1}&transportMode=pedestrian&destination={slat2},{slon2}&pedestrian[speed]=1.5" +
                "&return=summary&apikey=SIcIJgtLJENDVERoYyjlxblsIAFLbsP8wKCfnqU2iI8");
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            Stream stream = response.GetResponseStream();
            StreamReader sr = new StreamReader(stream);
            string json = sr.ReadToEnd();
            response.Close();

            string duration = "";
            string length = "";
            var strings = json.Split("\"duration\":")[1].Split(",\"length\":");
            duration = strings[0];
            length = strings[1].Split(",\"base")[0];
            return new Tuple<int, int>(int.Parse(duration), int.Parse(length));
        }

        public static void GetUsers()
        {
            sqlConnection.Open();
            var sqlCommand = new SqlCommand($"SELECT * From Пользователь", sqlConnection);
            ObservableCollection<User> result = new ObservableCollection<User>();
            var reader = sqlCommand.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new User()
                {
                    Id = (long)reader["Id"],
                    ChatId = default(long),
                    Type = null,
                    FinishStop = null,
                    FavStops = new List<int>()
                });
            }
            reader.Close();
            sqlConnection.Close();
            Users = result.ToList();
        }

        public static bool AddFav(int stopNumber, long chatId)
        {
            var user = Users.Where(i => i.ChatId == chatId).First();
            var id = 1;
            {
                sqlConnection.Open();
                var sqlCommand = new SqlCommand($"SELECT Id From Избранное", sqlConnection);
                var reader = sqlCommand.ExecuteReader();
                while (reader.Read())
                    if (id == (int)reader["Id"])
                        id++;
                reader.Close();
                sqlConnection.Close();
            }
            {
                sqlConnection.Open();
                var sqlCommand = new SqlCommand($"SELECT Остановка From Избранное WHERE Пользователь = {user.Id}", sqlConnection);
                var reader = sqlCommand.ExecuteReader();
                while (reader.Read())
                    user.FavStops.Add((int)reader["Остановка"]);
                reader.Close();
                sqlConnection.Close();
            }

            if (!user.FavStops.Contains(stopNumber))
            {
                sqlConnection.Open();
                var s = $"INSERT INTO Избранное VALUES({id},{user.Id},{stopNumber})";
                var sqlCommand = new SqlCommand(s, sqlConnection);
                ObservableCollection<Route> result = new ObservableCollection<Route>();
                var reader = sqlCommand.ExecuteNonQuery();
                sqlConnection.Close();
                return true;
            }
            else
                return false;
        }

        public static ObservableCollection<Route> GetAllRoutes(string transportType)
        {
            sqlConnection.Open();
            var sqlCommand = new SqlCommand($"SELECT * From Маршрут WHERE Тип = '{transportType}'", sqlConnection);
            ObservableCollection<Route> result = new ObservableCollection<Route>();
            var reader = sqlCommand.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new Route()
                {
                    Id = (int)reader["Id"],
                    Type = reader["Тип"].ToString(),
                    Sinature = (int)reader["Сигнатура"],
                    RouteNumber = (int)reader["Номер маршрута"],
                    FirstStop = reader["Первая конечная"].ToString(),
                    LastStop = reader["Вторая конечная"].ToString(),
                });
            }
            reader.Close();
            sqlConnection.Close();
            return result;
        }

        public static ObservableCollection<Stop> GetStopsByRoute(string transportType, int transportNumber, int direction)
        {
            sqlConnection.Open();
            var sqlCommand = new SqlCommand($"SELECT * FROM Подмаршрут WHERE [Id маршрута] = (SELECT [Id] FROM Маршрут Where Тип LIKE '{transportType}' AND [Номер маршрута] = {transportNumber}) " +
                $"AND Подмаршрут.[Постоянный набор остановок] = 1", sqlConnection);
            ObservableCollection<Subroute> result = new ObservableCollection<Subroute>();
            var reader = sqlCommand.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new Subroute()
                {
                    IdLink = (int)reader["Id перечня"],
                    IdRoute = (int)reader["Id маршрута"],
                    Direction = (int)reader["Направление"],
                    FullListStops = (int)reader["Постоянный набор остановок"],
                });
            }
            var selectedResult = result.Where(i => i.Direction == direction).Select(i => i.IdLink).First();
            reader.Close();

            sqlCommand = new SqlCommand($"SELECT * FROM [Перечень остановок] WHERE [Id перечня] = (SELECT TOP 1 Подмаршрут.[Id перечня] " +
                $"FROM Подмаршрут WHERE [Id перечня] = {selectedResult} AND [Id маршрута] = (SELECT [Id] FROM Маршрут Where Тип LIKE '{transportType}' AND [Номер маршрута] = {transportNumber}))", sqlConnection);
            ObservableCollection<LinkStop> resultLinkStop = new ObservableCollection<LinkStop>();
            reader = sqlCommand.ExecuteReader();
            while (reader.Read())
            {
                resultLinkStop.Add(new LinkStop()
                {
                    Id = (int)reader["Id"],
                    IdLink = (int)reader["Id перечня"],
                    IdStop = (int)reader["Id остановки"],
                });
            }
            reader.Close();

            ObservableCollection<Stop> resultStop = new ObservableCollection<Stop>();
            foreach (var stop in resultLinkStop)
            {
                sqlCommand = new SqlCommand($"SELECT * FROM Остановка WHERE id = {stop.IdStop}", sqlConnection);
                reader = sqlCommand.ExecuteReader();
                while (reader.Read())
                {
                    resultStop.Add(new Stop()
                    {
                        Id = (int)reader["id"],
                        FullName = reader["Полное название"].ToString(),
                        ShortName = reader["Укороченное название"].ToString(),
                        Direction = reader["Направление движения"].ToString(),
                        Type = reader["Тип"].ToString(),
                        Latitude = (double)reader["Широта"],
                        Longitude = (double)reader["долгота"],
                        Obligation = (int)reader["Обязательность"],
                    });
                }
                reader.Close();
            }
            sqlConnection.Close();
            return resultStop;
        }

        public static List<int> GetFavStopsId(long userId)
        {
            sqlConnection.Open();
            var sqlCommand = new SqlCommand($"SELECT Остановка FROM Избранное WHERE Пользователь = {userId}", sqlConnection);
            List<int> result = new List<int>();
            var reader = sqlCommand.ExecuteReader();
            while (reader.Read())
            {
                result.Add((int)reader["Остановка"]);
            }
            reader.Close();
            sqlConnection.Close();
            return result;
        }

        public static string GetStopName(int stopNumber)
        {
            var stopName = "";
            {
                sqlConnection.Open();
                var sqlCommand = new SqlCommand($"SELECT [Укороченное название],[Направление движения] FROM Остановка WHERE id = {stopNumber}", sqlConnection);
                var reader = sqlCommand.ExecuteReader();
                while (reader.Read())
                {
                    stopName = reader["Укороченное название"].ToString();
                    var direction = reader["Направление движения"].ToString();
                    direction = direction.Contains("в сторону") ? direction.Replace("в сторону", "до") : direction;
                    stopName += $" ({direction})";
                }
                reader.Close();
                sqlConnection.Close();
            }
            return stopName;
        }

        public static string GetStopInfo(int stopNumber, int routeNumber, string routeType)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://testapi.igis-transport.ru/telegram-Bhg3n3bZha50VAJD/prediction-stop/" + stopNumber.ToString());
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                Stream stream = response.GetResponseStream();
                StreamReader sr = new StreamReader(stream);
                string json = sr.ReadToEnd();
                response.Close();

                int transportId = 0;
                sqlConnection.Open();
                var sqlCommand = new SqlCommand($"SELECT Id From Маршрут WHERE [Номер маршрута] = {routeNumber} AND [Тип] = '{routeType}'", sqlConnection);
                var reader = sqlCommand.ExecuteReader();
                while (reader.Read())
                    transportId = (int)reader["Id"];
                reader.Close();
                sqlConnection.Close();
                var stringId = transportId.ToString();

                var splittedJson = json.Split($"\"{stringId}\":{{");
                if (splittedJson.Length > 1)
                {
                    var indexOfOpen = splittedJson[1].IndexOf('{');
                    var indexOfClosed = splittedJson[1].IndexOf('}');
                    indexOfOpen = indexOfOpen == -1 ? 1000 : indexOfOpen;

                    var index = indexOfOpen > indexOfClosed ? indexOfClosed : indexOfOpen;
                    index = splittedJson[1].IndexOf("seconds", 0, index);
                    if (index > 0)
                    {
                        int time;
                        var e = splittedJson[1].Split("seconds\":");
                        var s = e[1].Split(',');
                        int.TryParse(s[0], out time);
                        time = (time / 60) + 1;
                        string ending = "";
                        if (time % 10 == 1)
                            ending = "у";
                        if (time % 10 == 2 || time / 10 == 3 || time / 10 == 4)
                            ending = "ы";
                        return "~" + time.ToString() + " минут" + ending;
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine(ex.ToString()); }
            return "нет данных";
        }

        public static List<Tuple<int, string, string, string>> GetFavStopInfo(int stopNumber, long chatId)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://testapi.igis-transport.ru/telegram-Bhg3n3bZha50VAJD/prediction-stop/" + stopNumber.ToString());
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                Stream stream = response.GetResponseStream();
                StreamReader sr = new StreamReader(stream);
                string json = sr.ReadToEnd();
                response.Close();

                string pattern = @"(""\d+"":)";
                Regex regex = new Regex(pattern);
                var splittedJson = regex.Split(json);
                List<Tuple<int, string, string, string>> list = new List<Tuple<int, string, string, string>>();
                for (int i = 1; i < splittedJson.Length; i += 2)
                {
                    sqlConnection.Open();
                    var routeType = "";
                    var routeNumber = 0;
                    var time = 0;
                    var routeId = 0;
                    int.TryParse(splittedJson[i].Where(x => Char.IsDigit(x)).ToArray(), out routeId);
                    var sqlCommand = new SqlCommand($"SELECT [Тип],[Номер маршрута] From Маршрут WHERE [Id] = {routeId}", sqlConnection);
                    var reader = sqlCommand.ExecuteReader();
                    while (reader.Read())
                    {
                        routeType = reader["Тип"].ToString();
                        routeNumber = (int)reader["Номер маршрута"];
                    }
                    sqlConnection.Close();
                    var index = splittedJson[i + 1].IndexOf("seconds");
                    var item3 = "";
                    if (index > 0)
                    {
                        var e = splittedJson[i + 1].Split("seconds\":");
                        var s = e[1].Split(',');
                        int.TryParse(s[0], out time);
                        time = (time / 60) + 1;
                        string ending = "";
                        if (time % 10 == 1)
                            ending = "у";
                        if (time % 10 == 2 || time / 10 == 3 || time / 10 == 4)
                            ending = "ы";
                        item3 = "~" + time.ToString() + " минут" + ending;
                    }
                    else
                        item3 = "нет данных";
                    var item4 = "";
                    index = splittedJson[i + 1].IndexOf("finish_stop");
                    if (index > 0)
                    {
                        var user = Users.Where(i => i.ChatId == chatId).First();
                        var e = splittedJson[i + 1].Split("finish_stop\":");
                        var s = e[1].Split(',');
                        var finishStop = 0;
                        int.TryParse(s[0], out finishStop);
                        item4 = GetStopName(finishStop);
                    }
                    else
                    {
                        item4 = "не найдено";
                    }
                    list.Add(new Tuple<int, string, string, string>(routeNumber, routeType, item3, item4));
                }
                return list;
            }
            catch (Exception ex) { Console.WriteLine(ex.ToString()); }
            return null;
        }

        public static async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            // Некоторые действия
            Console.WriteLine(exception.ToString());
        }

        public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                if (update.CallbackQuery?.Message?.Date > dateOfRun || update.Message?.Date > dateOfRun)
                {
                    if (update.Type == UpdateType.Message && update?.Message?.Text != null)
                    {
                        await HandleMessage(botClient, update.Message);
                        return;
                    }

                    if (update.Type == UpdateType.CallbackQuery)
                    {
                        await HandleCallbackQuery(botClient, update.CallbackQuery);
                        return;
                    }
                    // некст строка будет крашиться если челика нет в бд и он попытается отправить геолокацию
                    if (update.Type == UpdateType.Message && Users.Where(i => i.Id == update?.Message?.From.Id).First() is User user && user.Request == CurrentExpectedRequest.Location && update?.Message?.Location is Location location)
                    {
                        user.Location = location;
                        var stops = FindNearestStops(location.Latitude, location.Longitude, update.Message.From.Id);
                        var buttons = stops.Select(i => new[] { InlineKeyboardButton.WithCallbackData($"{GetStopName(i.Id)}", $"find {i.Id}") });
                        var keyboard = new InlineKeyboardMarkup(buttons);
                        if (stops.Count() != 0)
                            await botClient.SendTextMessageAsync(update.Message.Chat, "Вот список ближайших остановок", replyMarkup: keyboard);
                        else
                            await botClient.SendTextMessageAsync(update.Message.Chat, "Остановки в пределах 500 метров не найдены");
                        user.Request = CurrentExpectedRequest.None;
                        return;
                        var nearestStopInfo = FindNearestStopRoute(stops, location.Latitude, location.Longitude);
                        await botClient.SendTextMessageAsync(update.Message.Chat, $"Ближайшая остановка: \n{nearestStopInfo.Item1.ShortName}, расстояние {nearestStopInfo.Item3}м, время пути ~ {nearestStopInfo.Item2} мин");
                        string s = $"https://yandex.ru/maps/44/izhevsk/?&mode=routes&rtext={location.Latitude}%2C{location.Longitude}~{nearestStopInfo.Item1.Latitude}%2C{nearestStopInfo.Item1.Longitude}&rtt=pd&ruri=~&z=17";
                        string b = "https://yandex.ru/maps/44/izhevsk/?&mode=routes&rtext=56.844519%2C53.297496~56.848274%2C53.297164&rtt=pd&ruri=~&z=17";
                        try
                        {
                            await botClient.SendPhotoAsync(update.Message.Chat, s);
                        }
                        catch (Exception ex) { }
                        return;
                    }
                }
            }
            catch (Exception ex) 
            {
                if (update.Type == UpdateType.CallbackQuery)
                    await botClient.SendTextMessageAsync(update?.CallbackQuery?.Message?.Chat.Id, ex.ToString());
                if (update.Type == UpdateType.Message)
                    await botClient.SendTextMessageAsync(update?.Message?.Chat.Id, ex.ToString());
                return;
            }
        }

        public static async Task HandleMessage(ITelegramBotClient botClient, Message message)
        {
            if (message.Text == "/start")
            {
                var welcome = "";
                sqlConnection.Open();
                if (!Users.Select(i => i.Id).Contains(message.From.Id))
                {
                    SqlCommand sqlCommand = new SqlCommand($"Insert into Пользователь values ({message.From.Id})", sqlConnection);
                    sqlCommand.ExecuteNonQuery();
                    Users.Add(new User() { Id = message.From.Id, ChatId = message.Chat.Id, Type = null, FinishStop = null, FavStops = new List<int>() });
                    welcome = $"Добро пожаловать, {message.From.Username}\n";
                }
                sqlConnection.Close();

                ReplyKeyboardMarkup keyboard = new ReplyKeyboardMarkup(new[]
                {
                    new KeyboardButton[] {"Маршруты","Поиск остановки", "Ближайшие остановки", "Избранные остановки"}
                })
                {
                    ResizeKeyboard = true
                };
                await botClient.SendTextMessageAsync(message.Chat.Id, welcome + "Для начала работы вы можете нажать на кнопки снизу под чатом и далее следовать инструкциям. Кроме того вы можете написать сообщение по типу 'трамвай', '27 автобус', 'троллейбус 10 спортивная' и получить соответствующий ответ.", replyMarkup: keyboard);
                return;
            }

            var user = Users.Where(i => i.Id == message.From.Id).First();
            user.ChatId = message.Chat.Id;

            if (message.Text == "Поиск остановки")
            {
                await botClient.SendTextMessageAsync(message.Chat.Id, "Введите название интересующей остановки");
                user.Request = CurrentExpectedRequest.StopName;
                return;
            }

            if (message.Text == "Ближайшие остановки")
            {

                InlineKeyboardMarkup keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Автобус/Троллейбус", "nearest bus"),
                        InlineKeyboardButton.WithCallbackData("Трамвай", "nearest tram"),
                    }
                });
                await botClient.SendTextMessageAsync(message.Chat.Id, "Выберите тип транспортного средства", replyMarkup: keyboard);
                return;
            }

            if (message.Text == "Избранные остановки")
            {
                var buttons = GetFavStopsId(message.From.Id).Select(i => new[] { InlineKeyboardButton.WithCallbackData($"{GetStopName(i)}", $"{i}_favStop") }).ToArray();
                InlineKeyboardMarkup keyboard = new InlineKeyboardMarkup(buttons);
                await botClient.SendTextMessageAsync(message.Chat.Id, "Ваши избранные остановки", replyMarkup: keyboard);
                return;
            }

            if (message.Text == "Маршруты")
            {
                InlineKeyboardMarkup keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Автобусы", "bus"),
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Троллейбусы", "trolleybus"),
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Трамваи", "tram"),
                    }
                });
                await botClient.SendTextMessageAsync(message.Chat.Id, "Выберите тип транспорта", replyMarkup: keyboard);
                return;
            }

            var messageText = message.Text.ToLower();
            var typeInRus = "";
            if (messageText.Contains("авт"))
            {
                user.Type = "bus";
                typeInRus = "авт";
            }
            else if (messageText.Contains("трол") || messageText.Contains("трал"))
            {
                user.Type = "trolleybus";
                typeInRus = "трол";
            }
            else if (messageText.Contains("трам"))
            {
                user.Type = "tram";
                typeInRus = "трам";
            }

            if (user.Request == CurrentExpectedRequest.Transport)
            {
                int routeNumber; // работает только после выбора типа транспорта, крашится если выбранного номера нет
                if (int.TryParse(string.Join("", message.Text.Where(c => char.IsDigit(c))), out routeNumber))
                {
                    var callbackQuery = new CallbackQuery();
                    callbackQuery.Message = message;
                    callbackQuery.Message.MessageId -= 1;
                    callbackQuery.Data = routeNumber + " route " + user.Type;
                    await HandleCallbackQuery(bot, callbackQuery);
                    user.Request = CurrentExpectedRequest.None;
                    return;
                }
            }

            if (user.Request == CurrentExpectedRequest.StopName)
            {
                var stopName = message.Text;
                var stops = new List<Tuple<int, string, string>>();
                var levenshtein = 99;
                if (user.Type == "" || user.Type == null)
                {
                    var allStops = GetAllStops(user.Id);
                    foreach (var stop in allStops)
                    {
                        var newInt = LevenshteinDistance(stop.ShortName, stopName);
                        levenshtein = newInt < levenshtein ? newInt : levenshtein;
                    }
                    foreach (var stop in allStops)
                    {
                        var newInt = LevenshteinDistance(stop.ShortName, stopName);
                        if (newInt == levenshtein)
                            stops.Add(new Tuple<int, string, string>(stop.Id, GetStopName(stop.Id), stop.ShortName));
                    }

                }
                else
                {
                    var allStops1 = GetStopsByRoute(user.Type, user.Route, 1);
                    var allStops2 = GetStopsByRoute(user.Type, user.Route, -1);

                    foreach (var stop in allStops1)
                    {
                        var newInt = LevenshteinDistance(stop.ShortName, stopName);
                        levenshtein = newInt < levenshtein ? newInt : levenshtein;
                    }
                    foreach (var stop in allStops2)
                    {
                        var newInt = LevenshteinDistance(stop.ShortName, stopName);
                        levenshtein = newInt < levenshtein ? newInt : levenshtein;
                    }

                    foreach (var stop in allStops1)
                    {
                        var newInt = LevenshteinDistance(stop.ShortName, stopName);
                        if (newInt == levenshtein)
                            stops.Add(new Tuple<int, string, string>(stop.Id, GetStopName(stop.Id), stop.ShortName));
                    }
                    foreach (var stop in allStops2)
                    {
                        var newInt = LevenshteinDistance(stop.ShortName, stopName);
                        if (newInt == levenshtein)
                            stops.Add(new Tuple<int, string, string>(stop.Id, GetStopName(stop.Id), stop.ShortName));
                    }
                    user.Route = user.Route;
                }
                var buttons = stops.Select(i => new[] { InlineKeyboardButton.WithCallbackData(i.Item2, $"{i.Item1}_requestStop'{i.Item3}'") });
                var keyboard = new InlineKeyboardMarkup(buttons);
                await botClient.SendTextMessageAsync(message.Chat.Id, "Вот какие остановки нашлись по вашему запросу:", replyMarkup: keyboard);
                user.Request = CurrentExpectedRequest.None;
                return;
            }

            if (user.Type == "bus" || user.Type == "trolleybus" || user.Type == "tram")
            {
                var data = messageText.Split(' ');
                if (data.Length == 1)
                {
                    await botClient.SendTextMessageAsync(message.Chat.Id, "Обработка...");

                    user.Request = CurrentExpectedRequest.Transport;
                    var callbackQuery = new CallbackQuery();
                    callbackQuery.Message = message;
                    callbackQuery.Message.MessageId += 1;
                    callbackQuery.Data = user.Type;
                    await HandleCallbackQuery(bot, callbackQuery);
                    return;
                }
                else if (data.Length == 2)
                {
                    int routeNumber = 0;
                    if (int.TryParse(string.Join("", data.Where(i => !i.Contains(typeInRus)).First().Where(c => char.IsDigit(c))), out routeNumber))
                    {
                        var callbackQuery = new CallbackQuery();
                        callbackQuery.Message = message;
                        callbackQuery.Message.MessageId += 1;
                        await botClient.SendTextMessageAsync(message.Chat.Id, "Обработка...");
                        callbackQuery.Data = routeNumber + " route " + user.Type;
                        await HandleCallbackQuery(bot, callbackQuery);
                        return;
                    }
                }
                else if (data.Length > 2)
                {
                    int routeNumber = 0;
                    if (int.TryParse(string.Join("", data.Where(i => !i.Contains(typeInRus)).First().Where(c => char.IsDigit(c))), out routeNumber))
                    {
                        var stopName = "";
                        for (int i = 2; i < data.Length; i++)
                            stopName += data[i] + " ";
                        stopName = stopName.Substring(0,stopName.Length - 1);

                        var stops = new List<Tuple<int, string, string>>();

                        var allStops1 = GetStopsByRoute(user.Type, routeNumber, 1);
                        var allStops2 = GetStopsByRoute(user.Type, routeNumber, -1);

                        var levenshtein = 99;
                        foreach (var stop in allStops1)
                        {
                            var newInt = LevenshteinDistance(stop.ShortName, stopName);
                            levenshtein = newInt < levenshtein ? newInt : levenshtein;
                        }
                        foreach (var stop in allStops2)
                        {
                            var newInt = LevenshteinDistance(stop.ShortName, stopName);
                            levenshtein = newInt < levenshtein ? newInt : levenshtein;
                        }

                        foreach (var stop in allStops1)
                        {
                            var newInt = LevenshteinDistance(stop.ShortName, stopName);
                            if (newInt == levenshtein)
                                stops.Add(new Tuple<int,string,string>(stop.Id,GetStopName(stop.Id),stop.ShortName));
                        }
                        foreach (var stop in allStops2)
                        {
                            var newInt = LevenshteinDistance(stop.ShortName, stopName);
                            if (newInt == levenshtein)
                                stops.Add(new Tuple<int, string, string>(stop.Id, GetStopName(stop.Id), stop.ShortName));
                        }

                        user.Route = routeNumber;

                        var buttons = stops.Select(i => new[] { InlineKeyboardButton.WithCallbackData(i.Item2, $"{i.Item1}_requestStop'{i.Item3}'") });
                        var keyboard = new InlineKeyboardMarkup(buttons);
                        await botClient.SendTextMessageAsync(message.Chat.Id, "Вот какие остановки нашлись по вашему запросу:", replyMarkup: keyboard);
                        user.Request = CurrentExpectedRequest.None;
                        user.Type = "";
                        return;
                    }
                }
            }

            await botClient.SendTextMessageAsync(message.Chat.Id, $"Вы написали:\n{message.Text}\nВаш запрос не принят");
        }

        public static async Task HandleCallbackQuery(ITelegramBotClient botClient, CallbackQuery callbackQuery)
        {
            var user = Users.Where(i => i.ChatId == callbackQuery.Message.Chat.Id).First();
            
            if (callbackQuery.Data == "types")
            {
                InlineKeyboardMarkup keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Автобусы", "bus"),
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Троллейбусы", "trolleybus"),
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Трамваи", "tram"),
                    }
                });
                await botClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, "Выберите тип транспорта", replyMarkup: keyboard);
                return;
            }

            if (callbackQuery.Data == "bus" || callbackQuery.Data == "trolleybus" || callbackQuery.Data == "tram")
            {
                InlineKeyboardMarkup keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Посмотреть все маршруты", "view routes"),
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Назад", "types"),
                        InlineKeyboardButton.WithCallbackData("На главную", "types"),
                    }
                });
                user.Type = callbackQuery.Data;
                user.Request = CurrentExpectedRequest.Transport;
                await botClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, "Вы выбрали: " + callbackQuery.Data + "\nВведите номер интересующего маршрута\nВы можете посмотреть все маршруты", replyMarkup: keyboard);
                return;
            }

            if (callbackQuery.Data.Contains("view routes"))
            {
                var allRoutes = GetAllRoutes(user.Type);
                var routeNumber = allRoutes.Select(route => route.RouteNumber).OrderBy(i => i).ToList();
                var keyboardButtons = new List<InlineKeyboardButton>();
                foreach (var route in routeNumber)
                {
                    keyboardButtons.Add(InlineKeyboardButton.WithCallbackData($"{route}", $"{route} route"));
                }
                
                InlineKeyboardMarkup keyboard = new InlineKeyboardMarkup(new[]
                {
                    keyboardButtons.AsEnumerable(),
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Назад", user.Type),
                        InlineKeyboardButton.WithCallbackData("На главную", "types"),
                    }
                });
                await botClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, "Вы выбрали: " + callbackQuery.Data.Split(' ').Last() + "\nВот список маршрутов:", replyMarkup: keyboard);
                return;
            }

            if (callbackQuery.Data.Contains("route"))
            {
                var direction = 'S';
                if (callbackQuery.Data.Last() == 'S')
                {
                    direction = 'B';
                    callbackQuery.Data = callbackQuery.Data.Substring(0, callbackQuery.Data.Length - 2);
                }
                if (callbackQuery.Data.Last() == 'B')
                    callbackQuery.Data = callbackQuery.Data.Substring(0, callbackQuery.Data.Length - 2);

                int routeNumber;
                int.TryParse(string.Join("", callbackQuery.Data.Where(c => char.IsDigit(c))), out routeNumber);
                user.Route = routeNumber;

                var allStops = GetStopsByRoute(user.Type, routeNumber, direction == 'S' ? 1 : -1);
                var keyboardButtons = new List<InlineKeyboardButton>();

                keyboardButtons.Add(InlineKeyboardButton.WithCallbackData($"{allStops[0].ShortName} (начальная)", $"{allStops[0].Id}_stop'{allStops[0].ShortName}'"));
                for (int i = 1; i < allStops.Count - 1; i++)
                    keyboardButtons.Add(InlineKeyboardButton.WithCallbackData($"{allStops[i].ShortName}", $"{allStops[i].Id}_stop'{allStops[i].ShortName}'"));
                keyboardButtons.Add(InlineKeyboardButton.WithCallbackData($"{allStops.Last().ShortName} (конечная)", $"{allStops.Last().Id}_stop'{allStops.Last().ShortName}'"));
                user.FinishStop = allStops.Last().ShortName;

                var enumerable = keyboardButtons.Select(i => new[] { i }).ToList();
                enumerable.Add(new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Назад", "view routes " + user.Type),
                        InlineKeyboardButton.WithCallbackData("Поменять направление", callbackQuery.Data + $" {direction}"),
                    });

                InlineKeyboardMarkup keyboard = new InlineKeyboardMarkup(enumerable);
                user.Request = CurrentExpectedRequest.StopName;
                await botClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, $"Вы выбрали: {routeNumber} {user.Type}\nВведите интересующую вас остановку.\nВы можете посмотреть все остановки.",
                    replyMarkup: keyboard);
                return;
            }

            if (callbackQuery.Data.Contains("stop"))
            {
                var data = callbackQuery.Data.Split('_');
                var stops = callbackQuery.Data.Split('\'');
                string stopName = stops[1];

                int stopNumber;
                int.TryParse(string.Join("", data[0].Where(c => char.IsDigit(c))), out stopNumber);
                user.Stop = stopNumber;

                int routeNumber = user.Route;
                string routeType = user.Type;

                InlineKeyboardMarkup keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Добавить в избранное", $"addfav_{stopNumber}_'{stopName}'"),
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Назад", $"{routeNumber} route " + callbackQuery.Data.Split('_').Last()),
                        InlineKeyboardButton.WithCallbackData("Обновить", callbackQuery.Data),
                    }
                });
                var time = GetStopInfo(stopNumber, routeNumber, routeType);
                if (!callbackQuery.Message.Text.Contains(time))
                    await botClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, $"Вы выбрали:\n" +
                        $"Остановка \"{stopName}\"\n" +
                        $"Маршрут \"{routeNumber} {routeType}\"\n" +
                        $"в направлении к конечной \"{user.FinishStop}\"\n" +
                        $"Прибудет через: {time}",
                        replyMarkup: keyboard);
                return;
            }

            if (callbackQuery.Data.Contains("requestStop"))
            {
                var data = callbackQuery.Data.Split('_');
                int stopNumber;
                int.TryParse(string.Join("", data[0].Where(c => char.IsDigit(c))), out stopNumber);

                var stopName = GetStopName(stopNumber);
                stopName = stopName.Substring(0, stopName.IndexOf(" (до"));

                InlineKeyboardMarkup keyboard = user.Route == 0 || user.Type == null || user.Type == "" ?
                    new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Добавить в избранное", $"addfav_{stopNumber}_'{stopName}'"),
                        InlineKeyboardButton.WithCallbackData("Обновить", callbackQuery.Data),
                    }
                }) : 
                    new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Добавить в избранное", $"addfav_{stopNumber}_'{stopName}'"),
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Назад", $"{user.Route} route " + user.Type),
                        InlineKeyboardButton.WithCallbackData("Обновить", callbackQuery.Data),
                    }
                });
                var list = GetFavStopInfo(stopNumber, user.ChatId);
                var s = "";
                if (list != null)
                {
                    foreach (var e in list)
                        if (e.Item1 == user.Route && e.Item2 == user.Type)
                            s += $"Маршрут \"{e.Item1} {e.Item2}\"\n" +
                             $"в направлении до остановки \"{e.Item4}\"\n" +
                             $"Прибудет через: {e.Item3}\n";
                }
                else s += $"Нет данных по маршрутам \n";
                await botClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, $"Вы выбрали:\n" +
                    $"Остановка \"{stopName}\"\n" + s,
                    replyMarkup: keyboard);
                return;
            }

            if (callbackQuery.Data.Contains("favStop"))
            {
                var data = callbackQuery.Data.Split('_');
                int stopNumber;
                int.TryParse(string.Join("", data[0].Where(c => char.IsDigit(c))), out stopNumber);

                var stopName = GetStopName(stopNumber);

                InlineKeyboardMarkup keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Удалить из избранное", $"deletefav_{stopNumber}"),
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Назад", $"types"),
                        InlineKeyboardButton.WithCallbackData("Обновить", callbackQuery.Data),
                    }
                });
                var list = GetFavStopInfo(stopNumber, user.ChatId);
                var s = "";
                if (list != null)
                    foreach (var e in list)
                        s += $"Маршрут \"{e.Item1} {e.Item2}\"\n" +
                             $"в направлении до остановки \"{e.Item4}\"\n" +
                             $"Прибудет через: {e.Item3}\n";
                else s += $"Нет данных по маршрутам \n";
                await botClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, $"Вы выбрали:\n" +
                    $"Остановка \"{stopName}\"\n" + s,
                    replyMarkup: keyboard);
                return;
            }

            if (callbackQuery.Data.Contains("addfav"))
            {
                var data = callbackQuery.Data.Split('_');
                var stopName = data[2].Split('\'')[1];

                if (!AddFav(int.Parse(data[1]), callbackQuery.Message.Chat.Id))
                    await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, text: $"Ошибка.\nОстановка {stopName} уже добавлена в избранное", showAlert: true);
                else
                    await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, text: $"Остановка {stopName} добавлена в избранное", showAlert: true);
                return;
            }

            if (callbackQuery.Data.Contains("backfind"))
            {
                var location = user.Location;
                var stops = FindNearestStops(location.Latitude, location.Longitude, user.Id);
                var buttons = stops.Select(i => new[] { InlineKeyboardButton.WithCallbackData($"{GetStopName(i.Id)}", $"find {i.Id}") });
                var keyboard = new InlineKeyboardMarkup(buttons);
                await botClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, "Вот список ближайших остановок", replyMarkup: keyboard);
                if (callbackQuery.Data.Last() != 'f')
                    await botClient.DeleteMessageAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId-1);
                return;
            }

            if (callbackQuery.Data.Contains("find"))
            {
                var data = callbackQuery.Data.Split(' ');
                var stopNumber = int.Parse(data[1]);

                var list = GetFavStopInfo(stopNumber, user.ChatId);
                var s = "";
                if (list != null)
                    foreach (var e in list)
                        s += $"Маршрут \"{e.Item1} {e.Item2}\"\n" +
                             $"в направлении до остановки \"{e.Item4}\"\n" +
                             $"Прибудет через: {e.Item3}\n";
                else s += $"Нет данных по маршрутам \n";
                var nearestRoute = FindNearestStopRoute(new List<Stop>() { GetStopById(stopNumber) }, user.Location.Latitude, user.Location.Longitude);

                
                
                string photo = $"https://yandex.ru/maps/44/izhevsk/?&mode=routes&rtext={user.Location.Latitude}%2C{user.Location.Longitude}~{nearestRoute.Item1.Latitude}%2C{nearestRoute.Item1.Longitude}&rtt=pd&ruri=~&z=17";
                string b = "https://yandex.ru/maps/44/izhevsk/?&mode=routes&rtext=56.844519%2C53.297496~56.848274%2C53.297164&rtt=pd&ruri=~&z=17";
                string photoIsSended = "";
                try
                {
                    await botClient.SendPhotoAsync(callbackQuery.Message.Chat.Id, photo);
                }
                catch (Exception ex) { Console.WriteLine("не удалось отправить картинку"); photoIsSended = "f"; }

                var keyboard = new InlineKeyboardMarkup(new[] { InlineKeyboardButton.WithCallbackData("Назад", "backfind"+photoIsSended), InlineKeyboardButton.WithCallbackData("Обновить", callbackQuery.Data) });
                await botClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, $"Остановка {GetStopName(stopNumber)}\n" +
                                                                                    $"Дистанция пешком: {nearestRoute.Item3}\n" +
                                                                                    $"Примерное время пути пешком: ~{nearestRoute.Item2} мин\n" + s, replyMarkup: keyboard);
                                

                return;
            }

            if (callbackQuery.Data.Contains("nearest"))
            {
                user.Type = callbackQuery.Data.Split(' ')[1];
                await botClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, "Отправьте свою геолокацию");
                user.Request = CurrentExpectedRequest.Location;
                return;
            }

            await botClient.SendTextMessageAsync(
                callbackQuery.Message.Chat.Id,
                $"You choose with data: {callbackQuery.Data}"
                );
            return;
        }


        static void Main(string[] args)
        {

            Console.WriteLine("Запущен бот " + bot.GetMeAsync().Result.FirstName);

            GetUsers();

            var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = { }, // receive all update types
            };
            bot.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                receiverOptions,
                cancellationToken
            );

            Console.ReadLine();
        }
    }
}
