using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Vorcyc.Metis.Classifiers.Text;


namespace Vorcyc.Metis.Storage.SQLiteStorage;

public class ArchiveEntity
{
    // PRIMARY KEY ([Id])
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // 让 EF 为主键在插入时生成值
    public long Id { get; set; }

    // NOT NULL
    public string Title { get; set; } = string.Empty;

    // NOT NULL
    public string Url { get; set; } = string.Empty;

    // NOT NULL
    public long TextLength { get; set; }

    // NOT NULL
    public long ImageCount { get; set; }

    // Mapped via value converter in DbContext to TEXT (UTC ISO-8601)
    public DateTimeOffset? PublishTime { get; set; }

    // NULL
    public string? Publisher { get; set; }


    public string Content { get; set; } = string.Empty;


    public PageContentCategory Category { get; set; }

}