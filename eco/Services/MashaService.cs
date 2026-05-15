using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace eco.Services
{
    public class MashaService
    {
        private const string TargetModel = "gpt-5.1";
        private const string ApiKey = private static readonly string ApiKey =
    Environment.GetEnvironmentVariable("MASHA_API_KEY") ?? "";
        private const string ApiUrl = "https://api.mashagpt.ru/v1/chat/completions";

        private readonly HttpClient _httpClient;

        public MashaService(HttpClient httpClient)
        {
            _httpClient = httpClient;

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        }

        public async Task<WasteResult?> AnalyzeAsync(string base64Image)
        {
            if (string.IsNullOrEmpty(base64Image)) return null;

            string prompt = @"Роль: Эксперт по сортировке мусора. Анализируй фото и отвечай строго в формате JSON.
                            Правила:
                            1. Цветной бак (Синий - пластик/Желтый - бумага/Зеленый-стекло/Красный - металл, Серый - другое) — только для ЧИСТЫХ и ОДНОРОДНЫХ предметов (не сортируй по виду пластика).
                            2. Если предмет требует подготовки — в поле 'Bin' пиши 'Нужна подготовка объекта!' и заполни поле 'Recommendation'.
                            3. Если предмет нельзя переработать — в поле 'Bin' пиши 'Серый' и заполни поле 'Comment'.
                            4. Если выбран цветной бак — поля 'Recommendation' и 'Comment' оставляй ПУСТЫМИ.
                            5. Если на фото то, что не сортируется (человек, конечность человека, снимок пустой поверхности, животное), то в поле в поле 'Bin' пиши 'Не утилизируется!' и заполни поле 'Comment'

                            Структура JSON:
                            {
                              ""Object"": ""название"",
                              ""Material"": ""материал"",
                              ""Bin"": ""цвет (только одно слово:Синий, Желтый, Зеленый, Красный или Серый)  или статус:Рано! Нужна подготовка объекта!"",
                              ""Recommendation"": ""что сделать (если нужно)"",
                              ""Comment"": ""почему нельзя (если нужно)""
                            }";

            var requestData = new
            {
                model = TargetModel,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "Ты эксперт по сортировке WasteWise. Отвечай СТРОГО в формате JSON по заданным правилам."
                    },
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{base64Image}" } }
                        }
                    }
                },
                response_format = new { type = "json_object" }
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync(ApiUrl, requestData);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"!!! MASHA ERROR: {response.StatusCode} - {errorBody}");
                    return null;
                }

                var rawResponse = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Masha success: {rawResponse}");

                string? content = ExtractContent(rawResponse);
                if (!string.IsNullOrEmpty(content))
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return JsonSerializer.Deserialize<WasteResult>(content, options);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"!!! MASHA EXCEPTION: {ex.Message}");
            }

            return null;
        }

        private string? ExtractContent(string rawResponse)
        {
            using var doc = JsonDocument.Parse(rawResponse);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var message = choices[0].GetProperty("message");
                return message.GetProperty("content").GetString();
            }
            return null;
        }
    }
}