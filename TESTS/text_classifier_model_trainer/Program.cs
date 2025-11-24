// See https://aka.ms/new-console-template for more information
using Vorcyc.Metis.Classifiers.Text;

train_toutiao();



static void train_bbc()
{
    var file = @"C:\Users\cyclo\Desktop\learn-ai-bbc\BBC News Train.csv";
    var lines = File.ReadAllLines(file).Skip(1);


    var dataset = new List<(string text, string category)>();

    foreach (var line in lines)
    {

        var parts = line.Split(',');
        if (parts.Length < 2) continue;
        var category = parts[2];
        var text = parts[1];

        dataset.Add((text, category));
    }


    var classifier = new TextClassifier(language: Language.English);


    classifier.Train(dataset, epochs: 50);
    classifier.Save("bbc_news_text_classifier.pt");


    var cate = classifier.Predict("worldcom ex-boss launches defence lawyers defending former worldcom chief bernie ebbers against a battery of fraud charges have called a company whistleblower as their first witness.  cynthia cooper  worldcom s ex-head of internal accounting  alerted directors to irregular accounting practices at the us telecoms giant in 2002. her warnings led to the collapse of the firm following the discovery of an $11bn (拢5.7bn) accounting fraud. mr ebbers has pleaded not guilty to charges of fraud and conspiracy.  prosecution lawyers have argued that mr ebbers orchestrated a series of accounting tricks at worldcom  ordering employees to hide expenses and inflate revenues to meet wall street earnings estimates. but ms cooper  who now runs her own consulting business  told a jury in new york on wednesday that external auditors arthur andersen had approved worldcom s accounting in early 2001 and 2002. she said andersen had given a  green light  to the procedures and practices used by worldcom. mr ebber s lawyers have said he was unaware of the fraud  arguing that auditors did not alert him to any problems.  ms cooper also said that during shareholder meetings mr ebbers often passed over technical questions to the company s finance chief  giving only  brief  answers himself. the prosecution s star witness  former worldcom financial chief scott sullivan  has said that mr ebbers ordered accounting adjustments at the firm  telling him to  hit our books . however  ms cooper said mr sullivan had not mentioned  anything uncomfortable  about worldcom s accounting during a 2001 audit committee meeting. mr ebbers could face a jail sentence of 85 years if convicted of all the charges he is facing. worldcom emerged from bankruptcy protection in 2004  and is now known as mci. last week  mci agreed to a buyout by verizon communications in a deal valued at $6.75bn.");

    Console.WriteLine(cate);

    var classifier2 = TextClassifier.Load("bbc_news_text_classifier.pt");
    var cate2 = classifier.Predict("brazil jobless rate hits new low brazil s unemployment rate fell to its lowest level in three years in december  according to the government.  the brazilian institute for geography and statistics (ibge) said it fell to 9.6% in december from 10.6% in november and 10.9% in december 2003. ibge also said that average monthly salaries grew 1.9% in december 2004 from december 2003. however  average monthly wages fell 1.8% in december to 895.4 reais ($332; 拢179.3) from november. tuesday s figures represent the first time that the unemployment rate has fallen to a single digit since new measurement rules were introduced in 2001. the unemployment rate has been falling gradually since april 2004 when it reached a peak of 13.1%. the jobless rate average for the whole of 2004 was 11.5%  down from 12.3% in 2003  the ibge said.  this improvement can be attributed to the country s strong economic growth  with the economy registering growth of 5.2% in 2004  the government said. the economy is expected to grow by about 4% this year. president luiz inacio lula da silva promised to reduce unemployment when he was elected two years ago. nevertheless  some analysts say that unemployment could increase in the next months.  the data is favourable  but a lot of jobs are temporary for the (christmas) holiday season  so we may see slightly higher joblessness in january and february   julio hegedus  chief economist with lopes filho & associates consultancy in rio de janeir  told reuters news agency. despite his leftist background  president lula has pursued a surprisingly conservative economic policy  arguing that in order to meet its social promises  the government needs to first reach a sustained economic growth. the unemployment rate is measured in the six main metropolitan areas of brazil (sao paolo  rio de janeiro  belo horizonte  recife  salvador and porto alegre)  where most of the population is concentrated.");

    Console.WriteLine(cate2);
}

static void train_toutiao()
{

    var file = "toutiao_cat_data.txt";

    var lines = File.ReadAllLines(file);

    var dataset = new List<(string text, string label)>();

    foreach (var line in lines)
    {
        var s = line.Split("_!_");
        var cate = s[2];
        var title = s[3];

        dataset.Add((title, cate));
    }

    // 创建分类器
    var classifier = new TextClassifier(vocabSize: 10000, embedDim: 128, hiddenDim: 256, language: Language.Chinese);

    // 训练
    classifier.Train(dataset, epochs: 10, batchSize: 64, maxSeqLen: 100, lr: 5e-4);

    classifier.Save("toutiao_news_title_classifier.pt");


    string testText = "来看看小姐姐扮演过多少魔兽世界角色！"; // 预期 娱乐


    // 加载模型并预测
    var loadedClassifier = TextClassifier.Load("toutiao_news_title_classifier.pt");
    string prediction2 = loadedClassifier.Predict(testText);
    Console.WriteLine($"Prediction (loaded model) for '{testText}': {prediction2}");

}