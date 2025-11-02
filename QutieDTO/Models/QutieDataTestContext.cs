using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;


namespace QutieDTO.Models;

public partial class QutieDataTestContext : DbContext
{
    public QutieDataTestContext(DbContextOptions<QutieDataTestContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AutomatedChecks> AutomatedChecks { get; set; }

    public virtual DbSet<ProcessedAutomatedCheck> ProcessedAutomatedChecks { get; set; }

    public virtual DbSet<UserMessageActivitySummary> UserMessageActivitySummary { get; set; }

    public virtual DbSet<UserVoiceActivitySummary> UserVoiceActivitySummary { get; set; }

    public virtual DbSet<GameFieldDefinition> GameFieldDefinition { get; set; }

    public virtual DbSet<ReactionRoles> ReactionRoles { get; set; }

    public virtual DbSet<FormSubmission> FormSubmissions { get; set; }

    public virtual DbSet<Channel> Channels { get; set; }

    public virtual DbSet<Event> Events { get; set; }

    public virtual DbSet<EventSignup> EventSignups { get; set; }

    public virtual DbSet<JoinToCreateChannel> JoinToCreateChannels { get; set; }

    public virtual DbSet<LevelToRoleMessage> LevelToRoleMessages { get; set; }

    public virtual DbSet<LevelToRoleVoice> LevelToRoleVoices { get; set; }

    public virtual DbSet<ReactionRoleConfig> ReactionRoleConfigs { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<AutoRole> AutoRoles { get; set; }

    public virtual DbSet<UserData> UserData { get; set; }

    public virtual DbSet<Xpconfig> Xpconfigs { get; set; }

    public virtual DbSet<Game> Games { get; set; }


    public virtual DbSet<AocData> AocData { get; set; }

    public virtual DbSet<WwmData> WwmData { get; set; }
    public virtual DbSet<AionData> AionData { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

        //Add games here
        modelBuilder.Entity<AocData>(entity =>
        {
            entity.HasKey(e => e.UserId);
        });

        modelBuilder.Entity<WwmData>(entity =>
        {
            entity.HasKey(e => e.UserId);
        });

        modelBuilder.Entity<AionData>(entity =>
        {
            entity.HasKey(e => e.UserId);
        });



        modelBuilder.Entity<ProcessedAutomatedCheck>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<AutomatedChecks>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<GameFieldDefinition>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(d => d.Game)
                .WithMany()
                .HasForeignKey(d => d.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AutoRole>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(d => d.Role)
                .WithMany()
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_AutoRoles_Roles");
        });

        modelBuilder.Entity<UserVoiceActivitySummary>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.VoiceMinutes).HasPrecision(18, 2);
            entity.HasOne(d => d.User).WithOne()
                .HasForeignKey<UserVoiceActivitySummary>(d => d.UserId);
        });


        modelBuilder.Entity<UserMessageActivitySummary>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(d => d.User).WithOne()
                .HasForeignKey<UserMessageActivitySummary>(d => d.UserId);
        });

        modelBuilder.Entity<UserData>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__UserData__1788CC4CFE5C22D3");

            entity.Property(e => e.UserId).ValueGeneratedNever();
            entity.Property(e => e.Karma).HasColumnName("karma");
            entity.Property(e => e.MessageRequiredXp).HasColumnName("MessageRequiredXP");
            entity.Property(e => e.MessageXp).HasColumnName("MessageXP");
            entity.Property(e => e.StoredMessageXp).HasColumnName("StoredMessageXP");
            entity.Property(e => e.StoredVoiceXp).HasColumnName("StoredVoiceXP");
            entity.Property(e => e.TotalVoiceTime).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.VoiceRequiredXp).HasColumnName("VoiceRequiredXP");
            entity.Property(e => e.VoiceXp).HasColumnName("VoiceXP");

            entity.HasOne(d => d.User).WithOne(p => p.UserData)
                .HasForeignKey<UserData>(d => d.UserId)
                .HasConstraintName("FK__UserData__UserId__6754599E");
        });

        modelBuilder.Entity<ReactionRoles>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<FormSubmission>(entity =>
        {
            entity.HasKey(e => e.SubmissionId);
        });

        modelBuilder.Entity<Channel>(entity =>
        {
            entity.HasKey(e => e.ChannelId).HasName("PK__Channels__38C3E814467B46EF");

            entity.Property(e => e.ChannelId).ValueGeneratedNever();
            entity.Property(e => e.ChannelName).HasMaxLength(255);

            entity.HasOne(d => d.Role).WithMany()
                .HasForeignKey(d => d.RoleId)
                .HasConstraintName("FK_Channels_RoleId");

            entity.HasOne(d => d.Game).WithMany(p => p.Channels)
                .HasForeignKey(d => d.GameId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_GameId");
        });

        modelBuilder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.EventId).HasName("PK__Events__7944C810CBB4C6C2");

            entity.Property(e => e.EventId).ValueGeneratedNever();
            entity.Property(e => e.Title).HasMaxLength(255).IsUnicode(false);

            entity.HasOne(d => d.Channel).WithMany(p => p.Events)
                .HasForeignKey(d => d.ChannelId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK__Events__ChannelI__61316BF4");
        });

        modelBuilder.Entity<Game>(entity =>
        {
            entity.HasKey(e => e.GameId).HasName("PK__Games__2AB897FDAD9C7AE1");

            entity.Property(e => e.GameName).HasMaxLength(255);

            entity.HasOne(d => d.Role).WithMany()
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK__Games__RoleId__662B2B3B");

            entity.HasOne(d => d.Channel).WithMany()
                .HasForeignKey(d => d.ChannelId);
        });

        modelBuilder.Entity<EventSignup>(entity =>
        {
            entity.HasKey(e => e.SignUpId).HasName("PK__EventSig__86B32DF396A83E29");

            entity.Property(e => e.SignUpId).ValueGeneratedNever();

            entity.HasOne(d => d.Event).WithMany(p => p.EventSignups)
                .HasForeignKey(d => d.EventId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__EventSign__UserN__640DD89F");

            entity.HasOne(d => d.User).WithMany(p => p.EventSignups)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_EventSignups_Users");
        });

        modelBuilder.Entity<JoinToCreateChannel>(entity =>
        {
            entity.HasKey(e => e.ChannelId).HasName("PK__JoinToCr__38C3E8148FB710C4");

            entity.Property(e => e.ChannelId).ValueGeneratedNever();
            entity.Property(e => e.Category).HasMaxLength(255);
            entity.Property(e => e.ChannelName).HasMaxLength(255);
        });

        modelBuilder.Entity<LevelToRoleMessage>(entity =>
        {
            entity.HasKey(e => e.Level).HasName("PK__LevelToR__AAF899635C9B3D52");

            entity.Property(e => e.Level).ValueGeneratedNever();

            entity.HasOne(d => d.Role).WithMany()
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK__LevelToRo__RoleI__6DCC4D03");
        });

        modelBuilder.Entity<LevelToRoleVoice>(entity =>
        {
            entity.HasKey(e => e.Level).HasName("PK__LevelToR__AAF8996322C58D08");

            entity.ToTable("LevelToRoleVoice");

            entity.Property(e => e.Level).ValueGeneratedNever();

            entity.HasOne(d => d.Role).WithMany()
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK__LevelToRo__RoleI__70A8B9AE");
        });

        modelBuilder.Entity<ReactionRoleConfig>(entity =>
        {
            entity.HasKey(e => e.ConfigId).HasName("PK__Reaction__C3BC335C37CE66A8");

            entity.ToTable("ReactionRoleConfig");

            entity.Property(e => e.Emoji).HasMaxLength(255);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("PK__Roles__8AFACE1A552CDDD5");

            entity.ToTable(tb => tb.HasTrigger("Delete_ReactionRoleConfig"));

            entity.Property(e => e.RoleId).ValueGeneratedNever();
            entity.Property(e => e.RoleName).HasMaxLength(255);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CC4CC7FF1BA6");

            entity.Property(e => e.UserId).ValueGeneratedNever();
            entity.Property(e => e.Avatar).HasMaxLength(255);
            entity.Property(e => e.DisplayName).HasMaxLength(255);

            entity.HasMany(d => d.Roles).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "UserRole",
                    r => r.HasOne<Role>().WithMany().HasForeignKey("RoleId"),
                    l => l.HasOne<User>().WithMany().HasForeignKey("UserId"),
                    j =>
                    {
                        j.HasKey("UserId", "RoleId").HasName("PK__UserRole__AF2760AD76768AF4");
                        j.ToTable("UserRoles");
                    });
        });

        modelBuilder.Entity<UserData>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__UserData__1788CC4CFE5C22D3");

            entity.Property(e => e.UserId).ValueGeneratedNever();
            entity.Property(e => e.Karma).HasColumnName("karma");
            entity.Property(e => e.MessageRequiredXp).HasColumnName("MessageRequiredXP");
            entity.Property(e => e.MessageXp).HasColumnName("MessageXP");
            entity.Property(e => e.StoredMessageXp).HasColumnName("StoredMessageXP");
            entity.Property(e => e.StoredVoiceXp).HasColumnName("StoredVoiceXP");
            entity.Property(e => e.TotalVoiceTime).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.VoiceRequiredXp).HasColumnName("VoiceRequiredXP");
            entity.Property(e => e.VoiceXp).HasColumnName("VoiceXP");

            entity.HasOne(d => d.User).WithOne(p => p.UserData)
                .HasForeignKey<UserData>(d => d.UserId)
                .HasConstraintName("FK__UserData__UserId__6754599E");
        });

        modelBuilder.Entity<Xpconfig>(entity =>
        {
            entity.HasKey(e => e.ConfigId).HasName("PK__XPConfig__C3BC335C2709512C");

            entity.ToTable("XPConfig");

            entity.Property(e => e.ConfigId).ValueGeneratedNever();
            entity.Property(e => e.MessageMaxXp).HasColumnName("MessageMaxXP");
            entity.Property(e => e.MessageMinXp).HasColumnName("MessageMinXP");
            entity.Property(e => e.VoiceMaxXp).HasColumnName("VoiceMaxXP");
            entity.Property(e => e.VoiceMinXp).HasColumnName("VoiceMinXP");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
