using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace eco.Services
{
    public class OllamaService
    {
        private const string TargetModel = "qwen3-vl:4b";
        private const string LocalUrl = "http://localhost:11434/api/chat";

        private readonly HttpClient _httpClient;

        public OllamaService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<WasteResult?> AnalyzeAsync(string base64Image)
        {
            if (string.IsNullOrEmpty(base64Image)) return null;

            string prompt = @"Роль: Эксперт по сортировке мусора. Анализируй фото и отвечай строго в формате JSON по шаблону.
                            Правила:
                            1. Предмет: Название, до 3 слов (запиши в 'Object').
                            2. Материал: Пластик, бумага, стекло, металл или другое (запиши в 'Material').
                            3. Цвет контейнера: Синий, желтый, зеленый, красный или серый (запиши в 'Bin').
                            4. Если нужна подготовка объекта перед выбросом, заполни поле 'Recommendation'.
                            5. Если переработка невозможна, обоснуй это в поле 'Comment'.

                            Структура JSON:
                            {
                              ""Object"": """",
                              ""Material"": """",
                              ""Bin"": """",
                              ""Recommendation"": """",
                              ""Comment"": """"
                            }";

            var requestData = new
            {
                model = TargetModel,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "Ты — эксперт по сортировке мусора. Отвечай СТРОГО в формате JSON."
                    },
                    new
                    {
                        role = "user",
                        content = prompt,
                        images = new[] { base64Image } 
                    }
                },
                stream = false,
                format = "json" 
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync(LocalUrl, requestData);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"!!! OLLAMA ERROR: {response.StatusCode} - {errorBody}");
                    return null;
                }

                var rawResponse = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Ollama success: {rawResponse}");

                string? content = ExtractContent(rawResponse);
                if (!string.IsNullOrEmpty(content))
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return JsonSerializer.Deserialize<WasteResult>(content, options);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"!!! OLLAMA EXCEPTION: {ex.Message}");
            }

            return null;
        }

        private string? ExtractContent(string rawResponse)
        {
            using var doc = JsonDocument.Parse(rawResponse);
            if (doc.RootElement.TryGetProperty("message", out var message))
            {
                return message.GetProperty("content").GetString();
            }
            return null;
        }
    }

    
}