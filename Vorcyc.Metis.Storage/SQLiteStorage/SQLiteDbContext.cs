using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Vorcyc.Metis.Classifiers.Text;

namespace Vorcyc.Metis.Storage.SQLiteStorage;

public class SQLiteDbContext : DbContext
{
    public SQLiteDbContext() { }
    public SQLiteDbContext(DbContextOptions<SQLiteDbContext> options) : base(options) { }

    public DbSet<ArchiveEntity> Archives => Set<ArchiveEntity>();

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


    private static T StringToEnum<T>(string value, T defaultValue) where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, true, out var result))
        {
            return result;
        }
        return defaultValue;
    }
}

