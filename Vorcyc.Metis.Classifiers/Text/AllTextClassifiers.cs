namespace Vorcyc.Metis.Classifiers.Text;

internal class AllTextClassifiers
{


    private const string BASE_FOLDER_MODEL_PATH = "model_zoo";

    private const string SUB_FOLDER_TEXT_CLASSIFICATION_PATH = "text_classifition";

    private readonly static string TEXT_CLASSIFICATION_BBC_PATH = System.IO.Path.Combine(BASE_FOLDER_MODEL_PATH, SUB_FOLDER_TEXT_CLASSIFICATION_PATH, "bbc_news_text_classifier.pt");

    private readonly static string TEXT_CLASSIFICATION_TOUTIAO_PATH = System.IO.Path.Combine(BASE_FOLDER_MODEL_PATH, SUB_FOLDER_TEXT_CLASSIFICATION_PATH, "toutiao_news_title_classifier.pt");


    private static Vorcyc.Metis.Classifiers.Text.TextClassifier s_ENG_BBC_Classifier
        = Vorcyc.Metis.Classifiers.Text.TextClassifier.Load(TEXT_CLASSIFICATION_BBC_PATH);

    private static Vorcyc.Metis.Classifiers.Text.TextClassifier s_CHN_TOUTIAO_Classifier
        = Vorcyc.Metis.Classifiers.Text.TextClassifier.Load(TEXT_CLASSIFICATION_TOUTIAO_PATH);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="text"></param>
    /// <returns>返回5个分类：business、politics、sport、tech、entertainment</returns>
    /// <remarks>
    /// 模型使用这套：
    /// https://www.kaggle.com/competitions/learn-ai-bbc
    /// 适用于英文的新闻分类
    /// </remarks>    
    public static Text.TextClassifier BBC_EnglishNewsClassifier => s_ENG_BBC_Classifier;



    /*
     * toutiao :
     * culture,entertainment,sports,finance,house,car,edu,tech,military,travel,world,agriculture,game,story
     * 
     * 
     */
    public static Text.TextClassifier Toutiao_ChineseNewsTitleClassifier => s_CHN_TOUTIAO_Classifier;




}
