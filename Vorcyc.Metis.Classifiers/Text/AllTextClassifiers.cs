namespace Vorcyc.Metis.Classifiers.Text;

/// <summary>
/// 全局文本分类器集合：提供预训练的中英文新闻标题分类器的懒加载单例访问。
/// </summary>
internal class AllTextClassifiers
{
    /// <summary>模型文件根目录。</summary>
    private const string BASE_FOLDER_MODEL_PATH = "model_zoo";

    /// <summary>文本分类模型子目录。</summary>
    private const string SUB_FOLDER_TEXT_CLASSIFICATION_PATH = "text_classifition";

    /// <summary>BBC 英文新闻分类模型路径。</summary>
    private readonly static string TEXT_CLASSIFICATION_BBC_PATH = System.IO.Path.Combine(BASE_FOLDER_MODEL_PATH, SUB_FOLDER_TEXT_CLASSIFICATION_PATH, "bbc_news_text_classifier.pt");

    /// <summary>头条中文新闻标题分类模型路径。</summary>
    private readonly static string TEXT_CLASSIFICATION_TOUTIAO_PATH = System.IO.Path.Combine(BASE_FOLDER_MODEL_PATH, SUB_FOLDER_TEXT_CLASSIFICATION_PATH, "toutiao_news_title_classifier.pt");

    /// <summary>BBC 英文分类器实例（应用启动时加载）。</summary>
    private static Vorcyc.Metis.Classifiers.Text.TextClassifier s_ENG_BBC_Classifier
        = Vorcyc.Metis.Classifiers.Text.TextClassifier.Load(TEXT_CLASSIFICATION_BBC_PATH);

    /// <summary>头条中文标题分类器实例（应用启动时加载）。</summary>
    private static Vorcyc.Metis.Classifiers.Text.TextClassifier s_CHN_TOUTIAO_Classifier
        = Vorcyc.Metis.Classifiers.Text.TextClassifier.Load(TEXT_CLASSIFICATION_TOUTIAO_PATH);

    /// <summary>
    /// BBC 英文新闻分类器，返回 5 个分类：business、politics、sport、tech、entertainment。
    /// </summary>
    /// <remarks>
    /// 模型基于 BBC 新闻数据集训练：https://www.kaggle.com/competitions/learn-ai-bbc
    /// 适用于英文新闻分类。
    /// </remarks>
    public static Text.TextClassifier BBC_EnglishNewsClassifier => s_ENG_BBC_Classifier;

    /// <summary>
    /// 头条中文新闻标题分类器，返回 14 个分类：
    /// culture、entertainment、sports、finance、house、car、edu、tech、military、travel、world、agriculture、game、story。
    /// </summary>
    public static Text.TextClassifier Toutiao_ChineseNewsTitleClassifier => s_CHN_TOUTIAO_Classifier;
}
