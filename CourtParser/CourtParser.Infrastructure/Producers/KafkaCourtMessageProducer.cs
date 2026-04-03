using System.Text.Json;
using Confluent.Kafka;
using CourtParser.Common.Kafka.Abstraction;
using CourtParser.Common.Kafka.KafkaHelpers;
using CourtParser.Common.Kafka.Messages;
using CourtParser.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CourtParser.Infrastructure.Producers;

public class KafkaCourtMessageProducer : IKafkaProducer
{
    private readonly IProducer<Null, string> _producer;
    private readonly KafkaOptions _config;
    private readonly ILogger<KafkaCourtMessageProducer> _logger;
    private readonly KafkaTopicHelpers _topicHelpers;
    private readonly HashSet<string> _verifiedTopics = [];

    public KafkaCourtMessageProducer(
        IOptions<KafkaOptions> config, 
        ILogger<KafkaCourtMessageProducer> logger,
        KafkaTopicHelpers topicHelpers)
    {
        _config = config.Value;
        _logger = logger;
        _topicHelpers = topicHelpers;

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _config.BootstrapServers,
            ClientId = _config.ClientId,
            MessageSendMaxRetries = _config.MessageSendMaxRetries,
            RetryBackoffMs = _config.RetryBackoffMs,
        };

        _producer = new ProducerBuilder<Null, string>(producerConfig)
            .SetErrorHandler(OnProducerError)
            .Build();
    }

    public async Task ProduceAsync(string topic, CourtCaseMessage message)
    {
        await EnsureTopicExistsAsync(topic);
        
        try
        {
            var jsonMessage = JsonSerializer.Serialize(message, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var kafkaMessage = new Message<Null, string> 
            { 
                Value = jsonMessage,
                Timestamp = new Timestamp(DateTime.UtcNow)
            };

            var result = await _producer.ProduceAsync(topic, kafkaMessage);
            
            _logger.LogDebug("Сообщение отправлено в топик {Topic}, partition {Partition}, offset {Offset}",
                result.Topic, result.Partition.Value, result.Offset.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка отправки сообщения в топик {Topic}", topic);
            throw;
        }
    }

    public async Task ProduceBatchAsync(string topic, List<CourtCaseMessage> messages)
    {
        await EnsureTopicExistsAsync(topic);
        
        var sendTasks = messages.Select(message => ProduceAsync(topic, message));
        
        try
        {
            await Task.WhenAll(sendTasks);
            _logger.LogInformation("Успешно отправлено {Count} сообщений в топик {Topic}", 
                messages.Count, topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибки при отправке batch в топик {Topic}", topic);
            throw;
        }
    }

    public async Task ProduceBatchWithRetryAsync(string topic, List<CourtCaseMessage> messages, int maxRetries = 3)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await ProduceBatchAsync(topic, messages);
                return;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.LogWarning(ex, "Попытка {Attempt}/{MaxRetries} не удалась. Повтор через 2 секунды...", 
                    attempt, maxRetries);
                await Task.Delay(2000);
            }
        }
        
        throw new Exception($"Не удалось отправить batch после {maxRetries} попыток");
    }

    private async Task EnsureTopicExistsAsync(string topic, bool forceCheck = false)
    {
        if (_verifiedTopics.Contains(topic) && !forceCheck)
            return;

        await _topicHelpers.EnsureTopicExistsAsync(
            _config.BootstrapServers, 
            topic,
            numPartitions: 3,
            replicationFactor: 1);
        
        _verifiedTopics.Add(topic);
    }

    private void OnProducerError(IProducer<Null, string> producer, Error error)
    {
        _logger.LogError("Kafka producer error: {Reason} (Code: {Code})", error.Reason, error.Code);
    }
    
    public async Task ProduceSingleMockMessageAsync(string topic)
    {
        var random = new Random();
        
        var mockMessage = new CourtCaseMessage
        {
            Title = "Хоринский районный суд Республики Бурятия - 9-243/2019 (М-529/2019)",
            Link = "https://www.xn--90afdbaav0bd1afy6eub5d.xn--p1ai/46716578/extended",
            CaseNumber = "9-243/2026",
            CourtType = "Железнодорожный районный суд г. Улан-Удэ",
            OriginalCaseLink = "https://test.sudrf.ru/test/1",
            HasDecision = true,
            DecisionLink = "https://example.com/test/decision/1",
            DecisionType = "Решение",
            FederalDistrict = "Сибирский федеральный округ",
            Region = "Республика Бурятия",
            Plaintiff = "Тестовый истец",
            Defendant = "Тестовый ответчик",
            ThirdParties = "Тестовые третьи лица",
            Representatives = "ТЕстовые представители",
            CaseResult = "Типо результат",
            ReceivedDate = DateTime.UtcNow.AddDays(-1),
            CaseCategory = "Тестовая категория",
            CaseSubcategory = "Тестовая подкатегория",
            DecisionContent = "<p style=\"TEXT-ALIGN: right; TEXT-INDENT: 0.5in\">№2-3887/19</p> <p style=\"TEXT-ALIGN: right; TEXT-INDENT: 0.5in\">04RS0007-01-2019-005092-17</p> <p style=\"TEXT-ALIGN: center; TEXT-INDENT: 0.5in\">РЕШЕНИЕ</p> <p style=\"TEXT-ALIGN: center; TEXT-INDENT: 0.5in\">Именем Российской Федерации</p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\">12 ноября 2019 г. г. Улан-Удэ</p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\"> Железнодорожный районный суд г. Улан-Удэ в составе судьи Гурман З.В., при секретаре Мануевой В.П., рассмотрев в открытом судебном заседании гражданское дело по иску Колбина <ФИО>А.С.</ФИО> к Пелеховой <ФИО>Т.П.</ФИО>, Колбину <ФИО>П.С.</ФИО> о восстановлении срока для принятия наследства, </p> <p style=\"TEXT-ALIGN: center; TEXT-INDENT: 0.5in\">установил: </p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\"> Обращаясь в суд с указанным иском, Колбин А.С. сослался на то, что 09 сентября 2018 г. умерла его бабушка Колбина Н.Н., после смерти которой открылось наследство в виде квартиры, расположенной по адресу: <адрес><адрес></адрес>. Он, являясь наследником второй очереди, в установленный шестимесячный срок не принял наследство, так как его тетя Пелехова Т.П. скрыла от него документы на квартиру, убедила в том, что квартира подарена ей, и он полагал, что какое-либо наследство после смерти Колбиной Н.Н. отсутствует. Впоследствии выяснилось, что Пелехова Т.П. его обманула, фактически Колбина Н.Н. квартиру ей не дарила. Поэтому просил восстановить ему срок для принятия наследства в виде квартиры по адресу: <адрес><адрес></адрес>, открывшегося после смерти Колбиной Н.Н. </p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\"> Истец Колбин А.Н. в судебном заседании исковые требования поддержал и суду пояснил, что срок для принятия наследства после смерти Колбиной Н.Н. пропущен им по уважительной причине, так как он был введен в заблуждение Пелеховой Т.П., скрывшей от него факт наличия наследства.</p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\">Ответчик Колбин П.С. против удовлетворения иска не возражал и суду пояснил, что и он пропустил срок для принятия наследства из-за обмана со стороны Пелеховой Т.П.</p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\">Ответчик Пелехова Т.П., надлежаще извещенная о времени и месте судебного разбирательства, в судебное заседание не явилась. </p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\">Выслушав пояснения участников процесса, исследовав письменные доказательства, суд приходит к следующему. </p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\">В силу п. 1 ст. 1110 ГК РФ при наследовании имущество умершего (наследство, наследственное имущество) переходит к другим лицам в порядке универсального правопреемства, то есть в неизменном виде как единое целое и в один и тот же момент, если из правил настоящего Кодекса не следует иное.</p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\">В соответствии с п. 1 ст. 1152 ГК РФ для приобретения наследства наследник должен его принять.</p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\">Принятие наследства осуществляется подачей по месту открытия наследства нотариусу или уполномоченному в соответствии с законом выдавать свидетельства о праве на наследство должностному лицу заявления наследника о принятии наследства либо заявления наследника о выдаче свидетельства о праве на наследство (п. 1 ст. 1153 ГК РФ).</p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\">Согласно п. 1 ст. 1154 ГК РФ наследство может быть принято в течение шести месяцев со дня открытия наследства.</p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\">В соответствии с п. 1 ст. 1155 ГК РФ по заявлению наследника, пропустившего срок, установленный для принятия наследства (статья 1154), суд может восстановить этот срок и признать наследника принявшим наследство, если наследник не знал и не должен был знать об открытии наследства или пропустил этот срок по другим уважительным причинам и при условии, что наследник, пропустивший срок, установленный для принятия наследства, обратился в суд в течение шести месяцев после того, как причины пропуска этого срока отпали.</p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\">Как разъяснено в п. 40 Постановления Пленума Верховного Суда РБ от 29 мая 2012 г. №9 «О судебной практике по делам о наследовании» требования о восстановлении срока принятия наследства и признании наследника принявшим наследство могут быть удовлетворены лишь при доказанности совокупности следующих обстоятельств:</p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\">а) наследник не знал и не должен был знать об открытии наследства или пропустил указанный срок по другим уважительным причинам. К числу таких причин следует относить обстоятельства, связанные с личностью истца, которые позволяют признать уважительными причины пропуска срока исковой давности: тяжелая болезнь, беспомощное состояние, неграмотность и т.п. (статья 205 ГК РФ), если они препятствовали принятию наследником наследства в течение всего срока, установленного для этого законом. Не являются уважительными такие обстоятельства, как кратковременное расстройство здоровья, незнание гражданско-правовых норм о сроках и порядке принятия наследства, отсутствие сведений о составе наследственного имущества и т.п.;</p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\">б) обращение в суд наследника, пропустившего срок принятия наследства, с требованием о его восстановлении последовало в течение шести месяцев после отпадения причин пропуска этого срока. Указанный шестимесячный срок, установленный для обращения в суд с данным требованием, не подлежит восстановлению, и наследник, пропустивший его, лишается права на восстановление срока принятия наследства.</p> <p style=\"TEXT-INDENT: 0.5in\"> Как установлено в судебном заседании, 09 сентября 2018 г. умерла Колбина Н.Н., являвшаяся собственником жилого помещения по адресу: <адрес><адрес></адрес>. </p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\"> В установленный законом шестимесячный срок с заявлением о принятии наследства к нотариусу истец Колбин А.С., являющийся внуком умершей Колбиной Н.Н., в силу п. 1 ст. 1143 ГК РФ относящийся к числу наследников второй очереди и осведомленный о смерти наследодателя, то есть о фактическом открытии наследства, не обратился. При этом доводы истца об уважительности причин пропуска им срока принятия наследства после смерти Колбиной Н.Н., связанных с его неосведомленностью о наличии наследственного имущества, не относятся к числу обстоятельств, связанных с личностью истца, которые бы в силу приведенных выше положений закона могли послужить основанием к восстановлению срока для принятия наследства. </p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\">В силу изложенного, исковое заявление Колбина А.Н. признается судом необоснованным и не подлежащим удовлетворению. </p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\"> На основании изложенного, руководствуясь ст.ст. 194-198 ГПК РФ, суд</p> <p style=\"TEXT-ALIGN: center; TEXT-INDENT: 0.5in\">решил:</p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\">Исковые требования Колбина <ФИО>А.С.</ФИО> оставить без удовлетворения. </p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\"> Решение может быть обжаловано в апелляционном порядке в Верховный суд Республики Бурятия в течение месяца с момента его принятия судом в окончательной форме.</p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\"> В окончательной форме решение суда принято 18 ноября 2019 г. </p> <p style=\"TEXT-ALIGN: justify; TEXT-INDENT: 0.5in\">Судья З.В.Гурман</p>",
            JudgeName = "Травников Дмитрий Олегович",
            Timestamp = DateTime.UtcNow,
            CaseMovements =
            [
                new()
                {
                    EventName = "Регистрация иска в суде",
                    EventResult = "Иск принят к производству",
                    Basis = $"ст. {random.Next(1,9999)} ГПК РФ",
                    EventDate = DateTime.UtcNow.AddDays(-12)
                },

                new()
                {
                    EventName = "Передача материалов судье",
                    EventResult = "Материалы переданы судье Тестову А.А.",
                    Basis = "",
                    EventDate = DateTime.UtcNow.AddDays(-random.Next(1,3))
                },
                
                new()
                {
                    EventName = "Вынесение решения",
                    EventResult = "Иск удовлетворён",
                    Basis = "Решение суда от " + DateTime.UtcNow.AddDays(-3).ToShortDateString(),
                    EventDate = DateTime.UtcNow.AddDays(-3)
                }
            ]
        };

        try
        {
            await ProduceAsync(topic, mockMessage);
            _logger.LogInformation("Успешно отправлено тестовое сообщение в топик {Topic}", topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка отправки тестового сообщения в топик {Topic}", topic);
            throw;
        }
    }

    public void Dispose()
    {
        _producer?.Flush(TimeSpan.FromSeconds(5));
        _producer?.Dispose();
    }
}