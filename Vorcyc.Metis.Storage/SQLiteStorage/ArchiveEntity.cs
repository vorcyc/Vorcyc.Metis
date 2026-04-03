using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Vorcyc.Metis.Classifiers.Text;


namespace Vorcyc.Metis.Storage.SQLiteStorage;

/// <summary>
/// 归档实体：对应 SQLite 数据库中的 Archives 表，存储爬取到的文章信息。
/// </summary>
public class ArchiveEntity
{
    /// <summary>
    /// 主键（自增），对应 SQLite 的 ROWID。
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>
    /// 文章标题（不可为空）。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 文章的绝对 URL（不可为空）。
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 正文纯文本的字符数（UTF-16 字符数）。
    /// </summary>
    public long TextLength { get; set; }

    /// <summary>
    /// 成功保存的图片数量。
    /// </summary>
    public long ImageCount { get; set; }

    /// <summary>
    /// 发布时间（存储为 UTC ISO-8601 文本，通过值转换器映射）。可为空。
    /// </summary>
    public DateTimeOffset? PublishTime { get; set; }

    /// <summary>
    /// 发布者/来源/作者。可为空。
    /// </summary>
    public string? Publisher { get; set; }

    /// <summary>
    /// 文章正文纯文本内容（不可为空，默认空字符串）。
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 文章分类的中文友好文本（如"科技"、"娱乐"）。
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 文章分类的枚举值，以整数形式存入 SQLite（支持 Flags 组合）。
    /// </summary>
    public PageContentCategory CategoryValue { get; set; }
}