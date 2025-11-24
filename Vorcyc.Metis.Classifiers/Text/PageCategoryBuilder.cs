namespace Vorcyc.Metis.Classifiers.Text;

/// <summary>
/// 页面内容类别（去除 news 前缀）。用于文本分类或内容标注。
/// </summary>
/// <remarks>
/// 已更新为按位标志（Flags）枚举，值使用 2 的幂以支持组合（位或操作）。
/// 注意：这将改变与外部数据集 ID 的一一映射关系，如需保持旧映射请在外部做转换。
/// </remarks>
[Flags]
public enum PageContentCategory
{
    /// <summary>
    /// 无分类
    /// </summary>
    None = 0,

    /// <summary>
    /// 教育（对应原始标签：news_edu）。
    /// </summary>
    Edu = 1 << 0,                 // 1

    /// <summary>
    /// 娱乐（对应原始标签：news_entertainment 或者是 BBC的 entertainment）。
    /// </summary>
    Entertainment = 1 << 1,       // 2

    /// <summary>
    /// 房产（对应原始标签：news_house）。
    /// </summary>
    House = 1 << 2,               // 4

    /// <summary>
    /// 科技（对应原始标签：news_tech ，或者是 BBC的 tech）。
    /// </summary>
    Tech = 1 << 3,                // 8

    /// <summary>
    /// 体育（对应原始标签：news_sports）。
    /// </summary>
    Sports = 1 << 4,              // 16

    /// <summary>
    /// 汽车（对应原始标签：news_car）。
    /// </summary>
    Car = 1 << 5,                 // 32

    /// <summary>
    /// 文化（对应原始标签：news_culture）。
    /// </summary>
    Culture = 1 << 6,             // 64

    /// <summary>
    /// 游戏（对应原始标签：news_game）。
    /// </summary>
    Game = 1 << 7,                // 128

    /// <summary>
    /// 旅游（对应原始标签：news_travel）。
    /// </summary>
    Travel = 1 << 8,              // 256

    /// <summary>
    /// 军事（对应原始标签：news_military）。
    /// </summary>
    Military = 1 << 9,            // 512

    /// <summary>
    /// 国际（对应原始标签：news_world）。
    /// </summary>
    World = 1 << 10,              // 1024

    /// <summary>
    /// 财经（对应原始标签：news_finance）。
    /// </summary>
    Finance = 1 << 11,            // 2048

    /// <summary>
    /// 农业、农村（对应原始标签：news_agriculture）。
    /// </summary>
    Agriculture = 1 << 12,        // 4096

    /// <summary>
    /// 故事（对应原始标签：news_story）。
    /// </summary>
    Story = 1 << 13,              // 8192

    /// <summary>
    /// 股票（对应原始标签：stock）。
    /// </summary>
    Stock = 1 << 14,              // 16384

    /// <summary>
    /// 国内政治（对应原始标签：news_domestic_politics）。
    /// </summary>
    DomesticPolitics = 1 << 15,   // 32768

    /// <summary>
    /// 政治，BBC 的 politics
    /// </summary>
    Politics = 1 << 16,           // 65536

    /// <summary>
    /// 体育，BBC 的 sport
    /// </summary>
    Sport = 1 << 17,              // 131072

    /// <summary>
    /// 商业，BBC 的 business
    /// </summary>
    Business = 1 << 18,           // 262144

    /// <summary>
    /// 全部（包含所有有效标志）。
    /// </summary>
    All = Edu
        | Entertainment
        | House
        | Tech
        | Sports
        | Car
        | Culture
        | Game
        | Travel
        | Military
        | World
        | Finance
        | Agriculture
        | Story
        | Stock
        | DomesticPolitics
        | Politics
        | Sport
        | Business
}

public static class PageCategoryBuilder
{


    private static PageContentCategory FromString(string category)
    {
        return category.ToLower() switch
        {
            "news_edu" => PageContentCategory.Edu,
            "news_entertainment" => PageContentCategory.Entertainment,
            "news_house" => PageContentCategory.House,
            "news_tech" => PageContentCategory.Tech,
            "news_sports" => PageContentCategory.Sports,
            "news_car" => PageContentCategory.Car,
            "news_culture" => PageContentCategory.Culture,
            "news_game" => PageContentCategory.Game,
            "news_travel" => PageContentCategory.Travel,
            "news_military" => PageContentCategory.Military,
            "news_world" => PageContentCategory.World,
            "news_finance" => PageContentCategory.Finance,
            "news_agriculture" => PageContentCategory.Agriculture,
            "news_story" => PageContentCategory.Story,
            "stock" => PageContentCategory.Stock,
            "news_domestic_politics" => PageContentCategory.DomesticPolitics,
            "tech" => PageContentCategory.Tech,
            "entertainment" => PageContentCategory.Entertainment,
            "politics" => PageContentCategory.Politics,
            "sport" => PageContentCategory.Sport,
            "business" => PageContentCategory.Business,
            _ => throw new NotImplementedException(),
        };
    }



    public static PageContentCategory Build(string title)
    {

        var language = Vorcyc.Metis.Classifiers.Text.LanguageDetector.Detect(title);

        switch (language)
        {
            case Classifiers.Text.LanguageDetector.LanguageType.Chinese:
                var cateStr = Vorcyc.Metis.Classifiers.Text.AllTextClassifiers.Toutiao_ChineseNewsTitleClassifier.Predict(title);
                return FromString(cateStr);
            case Text.LanguageDetector.LanguageType.English:
                var cateStr2 = Vorcyc.Metis.Classifiers.Text.AllTextClassifiers.BBC_EnglishNewsClassifier.Predict(title);
                return FromString(cateStr2);
            case Text.LanguageDetector.LanguageType.Unknown:
                return PageContentCategory.None;
            default:
                break;
        }

        return PageContentCategory.None;
    }




}
