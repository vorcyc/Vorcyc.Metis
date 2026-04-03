using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Vorcyc.Metis.Classifiers.Text;

namespace Vorcyc.Metis.Storage.SQLiteStorage;

/// <summary>
/// SQLite 数据库上下文：管理 <see cref="ArchiveEntity"/> 的增删改查与表结构映射。
/// </summary>
/// <remarks>
/// 默认连接本地 metis.sqlite3 数据库（优先从程序基目录查找，若不存在则回退到相对路径）。
/// </remarks>
public class SQLiteDbContext : DbContext
{
    /// <summary>
    /// 默认构造函数。
    /// </summary>
    public SQLiteDbContext() { }

    /// <summary>
    /// 使用外部配置选项创建实例（用于依赖注入场景）。
    /// </summary>
    /// <param name="options">数据库上下文配置选项。</param>
    public SQLiteDbContext(DbContextOptions<SQLiteDbContext> options) : base(options) { }

    /// <summary>
    /// 归档记录集合，对应 Archives 表。
    /// </summary>
    public DbSet<ArchiveEntity> Archives => Set<ArchiveEntity>();

    /// <summary>
    /// 配置 SQLite 连接字符串（当外部未配置时使用默认本地路径）。
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured)
        {
            return;
        }

        // Default: use DB in program's current directory
        var baseDir = AppContext.BaseDirectory;
        var localDbPath = Path.Combine(baseDir, "metis.sqlite3");
        string connectionString;

        if (File.Exists(localDbPath))
        {
            connectionString = $"Data Source={localDbPath};Cache=Shared;";
        }
        else
        {
            // Roll back to previous relative path
            connectionString = "Data Source=..\\..\\..\\metis.sqlite3;Cache=Shared;";
        }

        optionsBuilder.UseSqlite(connectionString);
    }

    /// <summary>
    /// 配置实体与数据表的映射关系（列名、类型、值转换器等）。
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ArchiveEntity>(entity =>
        {
            entity.ToTable("Archives");

            entity.HasKey(e => e.Id)
                  .HasName("sqlite_master_PK_Archives");

            // INTEGER PRIMARY KEY for SQLite auto-increment (ROWID)
            entity.Property(e => e.Id)
                  .HasColumnName("Id")
                  .HasColumnType("INTEGER")
                  .ValueGeneratedOnAdd();

            entity.Property(e => e.Title)
                  .IsRequired()
                  .HasColumnName("Title")
                  .HasColumnType("TEXT");

            entity.Property(e => e.Url)
                  .IsRequired()
                  .HasColumnName("Url")
                  .HasColumnType("TEXT");

            entity.Property(e => e.TextLength)
                  .HasColumnName("TextLength")
                  .HasColumnType("INTEGER");

            entity.Property(e => e.ImageCount)
                  .HasColumnName("ImageCount")
                  .HasColumnType("INTEGER");

            // ValueConverter: always store UTC ISO-8601 text so string comparison is chronological
            entity.Property(e => e.PublishTime)
                  .HasColumnName("PublishTime")
                  .HasColumnType("TEXT")
                  .HasConversion(
                      toProvider => toProvider.HasValue
                          ? toProvider.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                          : null,
                      fromProvider => string.IsNullOrEmpty(fromProvider)
                          ? null
                          : DateTimeOffset.Parse(fromProvider, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                  )
                  .IsRequired(false);

            entity.Property(e => e.Publisher)
                  .HasColumnName("Publisher")
                  .HasColumnType("TEXT")
                  .IsRequired(false);


            // Ensure Content column mapping (TEXT NOT NULL)
            entity.Property(e => e.Content)
                  .HasColumnName("Content")
                  .HasColumnType("TEXT")
                  .IsRequired();

            // Category: TEXT NOT NULL (string)
            entity.Property(e => e.Category)
                  .HasColumnName("Category")
                  .HasColumnType("TEXT")
                  .IsRequired();

            // CategoryValue: INTEGER NOT NULL (enum stored as int)
            entity.Property(e => e.CategoryValue)
                  .HasColumnName("CategoryValue")
                  .HasColumnType("INTEGER")
                  .IsRequired();



        });
    }


    /// <summary>
    /// 将字符串安全转换为指定的枚举类型，解析失败时返回默认值。
    /// </summary>
    /// <typeparam name="T">目标枚举类型。</typeparam>
    /// <param name="value">待解析的字符串。</param>
    /// <param name="defaultValue">解析失败时的默认值。</param>
    /// <returns>解析后的枚举值。</returns>
    private static T StringToEnum<T>(string value, T defaultValue) where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, true, out var result))
        {
            return result;
        }
        return defaultValue;
    }
}

