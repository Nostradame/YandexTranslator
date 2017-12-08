<Query Kind="Program">
  <NuGetReference>Newtonsoft.Json</NuGetReference>
  <NuGetReference>RestSharp</NuGetReference>
</Query>

const string ApiUrl = @"https://translate.yandex.net/api/v1.5/tr.json/translate";
List<string> ApiKeys = new List<string>(){};
const int MaxTranslatedCharsPerRequest = 10000;	// количество переводимых символов на запрос
const int MaxTranslatedCharsPerKey = 950000;	// количество переводимых символов на один ключ

delegate bool TranslatorDelegate(RestSharp.RestClient client, Row originRow, string apiKey, Row tranlstaedRow);

void Main()
{
	var client = new RestSharp.RestClient(ApiUrl);
	Process(client,
			Translate,
			@"c:\[demo].[DbStringCustomization].csv", 
			@"c:\[demo].[DbStringCustomization].csv");
}


string Translate(RestSharp.RestClient client, Row originRow, string apiKey)
{
	if (client is null)
		throw new ArgumentNullException(nameof(client));
		
	if (originRow is null)
		throw new ArgumentNullException(nameof(originRow));

	var request = new RestSharp.RestRequest(RestSharp.Method.POST);
	request.AddParameter("key", apiKey);
	request.AddParameter("text", originRow.Text);
	request.AddParameter("lang", "ru-en");
	request.AddParameter("format", "plain");

	RestSharp.IRestResponse response = client.Execute<Row>(request);
	var recievedData = ((RestSharp.RestResponse<Row>)response).Data;
	
	var isSuccess = recievedData.Code == 200;
	
	return isSuccess
		// ввиду того, что в заголовках можно пережавать несколько заголовков text, то и результатом является массив
		? ((string[])Newtonsoft.Json.JsonConvert.DeserializeObject(recievedData.Text, typeof(string[])))[0]
		: originRow.Text;
}

string TranslateStub(RestSharp.RestClient client, Row originRow, string apiKey)
{
	return originRow.Text;
}

/// <summary>Точка входа для перевода</summary>
/// <param name="client">REST-клиент для работы с API Yandex</param>
/// <param name="translator">Метод получения перевода</param>
/// <param name="fileNameForTranslating">Путь к файлу, который необходимо перевести</param>
/// <param name="fileNameForTranslated">Путь к файлу, куда будет сохранен перевод</param>
void Process(RestSharp.RestClient client, Func<RestSharp.RestClient, Row, string, string> translator, string fileNameForTranslating, string fileNameForTranslated)
{
	if (client is null)
		throw new ArgumentNullException(nameof(client));
	
	if (!File.Exists(fileNameForTranslating))
		throw new FileNotFoundException(fileNameForTranslating);
	
	using (var sw = new StreamWriter(fileNameForTranslated, false, Encoding.UTF8, 4098))
	using (var fs = File.OpenRead(fileNameForTranslating))
	using (var sr = new StreamReader(fs))
	{
		int idx = 0;
		int totalTranslatedCharsPerKey = 0;
		var text = string.Empty;
		var isFirstRow = true;
		while (true)
		{
			// вычитываем следующую партию данных для перевода
			var buffer = sr.ReadLine();

			if (buffer is null)
			{
				if (text.Length != 0)
				{
					var translatedText = translator(client, new Row() { Code = 0, Lang = "ru-en", Text = text }, ApiKeys[idx]);
					totalTranslatedCharsPerKey = 0;
					sw.WriteLine(translatedText);
					text = string.Empty;
				}
				break;
			}
			else
			{
				if (isFirstRow)
				{
					buffer += "\r\n";
					isFirstRow = false;
				}
				
				if(!string.IsNullOrWhiteSpace(text))
				{
					buffer += "\r\n";
				}				
			}
			
			// если общее количество переведенных символов превосходит MaxTranslatedCharsPerKey, то необходимо выполнить смену ключа API
			if (totalTranslatedCharsPerKey + buffer.Length > MaxTranslatedCharsPerKey)
			{
				// записываем, все что подготовлено для перевода, а новую порцию пропускаем, в случае если кончились ключи,
				// или меняем ключ и новую порцию оставляем до следующего запроса
				var translatedText = translator(client, new Row() { Code = 0, Lang = "ru-en", Text = text }, ApiKeys[idx]);
				totalTranslatedCharsPerKey = 0;
				sw.WriteLine(translatedText);
				text = string.Empty;
				
				// если кончились ключи для API, то перевод необходимо прекратить
				if (ApiKeys.Count == ++idx)
				{
					break;
				}
				else
				{
					totalTranslatedCharsPerKey = 0;
				}
			}

			// проверка не превышен ли размер максимального количества переводимых символов на один запрос, или остались ли еще данные для перевода
			if ((text + buffer).Length > MaxTranslatedCharsPerRequest)
			{
				var translatedText = translator(client, new Row() { Code = 0, Lang = "ru-en", Text = text }, ApiKeys[idx]);
				totalTranslatedCharsPerKey += text.Length;
				sw.WriteLine(translatedText);
				text = buffer;
			}
			else
			{
				text += buffer;
			}		
		}

	}	
}

class Row
{
	[RestSharp.Deserializers.DeserializeAs(Name = "code")]
	public int Code { set; get;}
	[RestSharp.Deserializers.DeserializeAs(Name = "lang")]
	public string Lang { get; set; }
	[RestSharp.Deserializers.DeserializeAs(Name = "text")]
	public string Text { get; set; }
}