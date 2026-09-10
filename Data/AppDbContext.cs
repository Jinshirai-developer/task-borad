using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using TaskApi.Models;

namespace TaskApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityUserContext<UserProfile, int>(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskUndoEntry> TaskUndoEntries => Set<TaskUndoEntry>();

    public DbSet<PetProfile> PetProfiles => Set<PetProfile>();

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<EmailOutboxMessage> EmailOutbox => Set<EmailOutboxMessage>();

    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamBilling> TeamBillings => Set<TeamBilling>();
    public DbSet<BillingEventReceipt> BillingEventReceipts => Set<BillingEventReceipt>();

    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    public DbSet<CompletionReward> CompletionRewards => Set<CompletionReward>();

    public DbSet<TaskTagDefinition> TaskTagDefinitions => Set<TaskTagDefinition>();
    public DbSet<PetCollection> PetCollections => Set<PetCollection>();
    public DbSet<PetRewardChoice> PetRewardChoices => Set<PetRewardChoice>();
    public DbSet<PetMemory> PetMemories => Set<PetMemory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<TeamBilling>(entity =>
        {
            entity.ToTable("account_billing");
            entity.HasKey(item => item.UserProfileId);
            entity.Property(item => item.UserProfileId).ValueGeneratedNever();
            entity.Property(item => item.MetadataScope).HasMaxLength(10);
            entity.Property(item => item.Status).HasMaxLength(30);
            entity.Property(item => item.AttemptId).HasMaxLength(36);
            entity.Property(item => item.PriceId).HasMaxLength(255);
            entity.Property(item => item.ReturnOrigin).HasMaxLength(2048);
            entity.Property(item => item.SessionId).HasMaxLength(255);
            entity.Property(item => item.SubscriptionId).HasMaxLength(255);
            entity.Property(item => item.OperationToken).HasMaxLength(36);
            entity.HasIndex(item => item.AttemptId).IsUnique();
            entity.HasIndex(item => item.SubscriptionId).IsUnique();
            entity.HasOne<UserProfile>().WithOne().HasForeignKey<TeamBilling>(item => item.UserProfileId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<BillingEventReceipt>(entity =>
        {
            entity.ToTable("billing_event_receipts");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(255);
            entity.HasIndex(item => item.ProcessedAt);
            entity.HasOne<UserProfile>().WithMany().HasForeignKey(item => item.UserProfileId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<TaskUndoEntry>(entity =>
        {
            entity.ToTable("task_undo_entries");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Snapshot).HasMaxLength(1000000).IsRequired();
            entity.HasIndex(item => item.ExpiresAt);
            entity.HasIndex(item => new { item.UserProfileId, item.ExpiresAt });
            entity.HasOne<UserProfile>().WithMany().HasForeignKey(item => item.UserProfileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Team>().WithMany().HasForeignKey(item => item.TeamId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PetCollection>(entity =>
        {
            entity.ToTable("pet_collections", table =>
            {
                table.HasCheckConstraint("CK_pet_collections_stage", "\"Stage\" IN ('base','explorer','grown','festival')");
                table.HasCheckConstraint("CK_pet_collections_levels", "(\"HatLevel\" IS NULL OR \"HatLevel\" BETWEEN 1 AND 20) AND (\"BowLevel\" IS NULL OR \"BowLevel\" BETWEEN 1 AND 20) AND (\"MatLevel\" IS NULL OR \"MatLevel\" BETWEEN 1 AND 20)");
            });
            entity.HasKey(item => item.PetProfileId);
            entity.Property(item => item.Stage).HasMaxLength(20).IsRequired();
            entity.HasOne<PetProfile>().WithOne().HasForeignKey<PetCollection>(item => item.PetProfileId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PetRewardChoice>(entity =>
        {
            entity.ToTable("pet_reward_choices", table =>
            {
                table.HasCheckConstraint("CK_pet_reward_choices_level", "\"Level\" BETWEEN 1 AND 20");
                table.HasCheckConstraint("CK_pet_reward_choices_choice", "\"Choice\" IN ('hat','bow','mat')");
            });
            entity.HasKey(item => new { item.PetProfileId, item.Level });
            entity.Property(item => item.Choice).HasMaxLength(10).IsRequired();
            entity.HasOne<PetProfile>().WithMany().HasForeignKey(item => item.PetProfileId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PetMemory>(entity =>
        {
            entity.ToTable("pet_memories");
            entity.HasKey(item => new { item.PetProfileId, item.Key });
            entity.Property(item => item.Key).HasMaxLength(30);
            entity.HasOne<PetProfile>().WithMany().HasForeignKey(item => item.PetProfileId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<TaskTagDefinition>(entity =>
        {
            entity.ToTable("task_tag_definitions", table =>
            {
                table.HasCheckConstraint("CK_task_tag_definitions_scope", "(user_profile_id IS NOT NULL AND team_id IS NULL) OR (user_profile_id IS NULL AND team_id IS NOT NULL)");
                table.HasCheckConstraint("CK_task_tag_definitions_name", "char_length(name) BETWEEN 1 AND 300 AND char_length(normalized_name) BETWEEN 1 AND 300");
            });
            entity.HasKey(tag => tag.Id);
            entity.Property(tag => tag.Id).HasColumnName("id");
            entity.Property(tag => tag.UserProfileId).HasColumnName("user_profile_id");
            entity.Property(tag => tag.TeamId).HasColumnName("team_id");
            entity.Property(tag => tag.Name).HasColumnName("name").HasMaxLength(300).IsRequired();
            entity.Property(tag => tag.NormalizedName).HasColumnName("normalized_name").HasMaxLength(300).IsRequired();
            entity.Property(tag => tag.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(tag => new { tag.UserProfileId, tag.NormalizedName }).IsUnique().HasFilter("user_profile_id IS NOT NULL");
            entity.HasIndex(tag => new { tag.TeamId, tag.NormalizedName }).IsUnique().HasFilter("team_id IS NOT NULL");
            entity.HasOne<UserProfile>().WithMany().HasForeignKey(tag => tag.UserProfileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Team>().WithMany().HasForeignKey(tag => tag.TeamId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CompletionReward>(entity =>
        {
            entity.ToTable("completion_rewards", table =>
            {
                table.HasCheckConstraint("CK_completion_rewards_experience", "experience = 25");
                table.HasCheckConstraint("CK_completion_rewards_energy", "energy_granted BETWEEN 0 AND 100");
                table.HasCheckConstraint("CK_completion_rewards_dates", "revoked_at IS NULL OR revoked_at >= awarded_at");
            });
            entity.HasKey(reward => reward.Id);
            entity.Property(reward => reward.Id).HasColumnName("id");
            entity.Property(reward => reward.TaskId).HasColumnName("task_id");
            entity.Property(reward => reward.UserProfileId).HasColumnName("user_profile_id");
            entity.Property(reward => reward.AwardedAt).HasColumnName("awarded_at");
            entity.Property(reward => reward.RevokedAt).HasColumnName("revoked_at");
            entity.Property(reward => reward.Experience).HasColumnName("experience");
            entity.Property(reward => reward.EnergyGranted).HasColumnName("energy_granted");
            entity.HasIndex(reward => reward.TaskId).IsUnique();
            entity.HasIndex(reward => new { reward.UserProfileId, reward.RevokedAt });
            entity.HasOne(reward => reward.Task).WithMany().HasForeignKey(reward => reward.TaskId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<UserProfile>().WithMany().HasForeignKey(reward => reward.UserProfileId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<Team>(entity =>
        {
            entity.ToTable("teams");
            entity.HasKey(team => team.Id);
            entity.Property(team => team.Name).HasMaxLength(100).IsRequired();
            entity.Property(team => team.InviteCodeHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(team => team.InviteCodeHash).IsUnique();
            entity.Property(team => team.Version).IsRowVersion();
            entity.HasOne<UserProfile>().WithMany().HasForeignKey(team => team.OwnerUserProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<TeamMember>(entity =>
        {
            entity.ToTable("team_members");
            entity.HasKey(member => new { member.TeamId, member.UserProfileId });
            entity.HasOne<Team>().WithMany().HasForeignKey(member => member.TeamId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserProfile>().WithMany().HasForeignKey(member => member.UserProfileId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<EmailOutboxMessage>(entity =>
        {
            entity.ToTable("email_outbox");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Purpose).HasMaxLength(30);
            entity.Property(message => message.ProtectedPayload).IsRequired();
            entity.HasIndex(message => message.NextAttemptAt);
            entity.HasIndex(message => new { message.UserProfileId, message.Purpose }).IsUnique();
            entity.HasOne<UserProfile>().WithMany().HasForeignKey(message => message.UserProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.ToTable("tasks", table =>
            {
                table.HasCheckConstraint("CK_tasks_status", "\"Status\" IN (0, 1, 2)");
                table.HasCheckConstraint("CK_tasks_priority", "priority IN (0, 1, 2)");
                table.HasCheckConstraint("CK_tasks_completion_status", "is_completed = (\"Status\" = 2)");
                table.HasCheckConstraint("CK_tasks_owner_or_team", "user_profile_id IS NOT NULL OR team_id IS NOT NULL");
            });

            entity.HasKey(task => task.Id);

            entity.Property(task => task.Id)
                .HasColumnName("id");

            entity.Property(task => task.UserProfileId)
                .HasColumnName("user_profile_id");

            entity.Property(task => task.TeamId).HasColumnName("team_id");
            entity.Property(task => task.AssigneeUserProfileId).HasColumnName("assignee_user_profile_id");
            entity.HasOne<UserProfile>().WithMany().HasForeignKey(task => task.AssigneeUserProfileId).OnDelete(DeleteBehavior.SetNull);
            entity.Property(task => task.ChecklistJson).HasColumnName("checklist_json").HasMaxLength(20000).HasDefaultValue("[]");
            entity.Property(task => task.CompanionJson).HasColumnName("companion_json").HasMaxLength(120000).HasDefaultValue("{}");
            entity.HasIndex(task => task.TeamId);
            entity.HasOne<Team>().WithMany().HasForeignKey(task => task.TeamId).OnDelete(DeleteBehavior.Cascade);

            entity.Property(task => task.Title)
                .HasColumnName("title")
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(task => task.Description)
                .HasColumnName("description");

            entity.Property(task => task.IsCompleted)
                .HasColumnName("is_completed");

            entity.Property(task => task.DueDate)
                .HasColumnName("due_date");

            entity.Property(task => task.Priority)
                .HasColumnName("priority");

            entity.Property(task => task.Tags)
                .HasColumnName("tags")
                .HasMaxLength(300);

            entity.Property(task => task.CompletionRewardedAt)
                .HasColumnName("completion_rewarded_at");

            entity.Property(task => task.CreatedAt)
                .HasColumnName("created_at");

            entity.Property(task => task.UpdatedAt)
                .HasColumnName("updated_at");

            entity.Property(task => task.Version)
                .IsRowVersion();

            entity.HasIndex(task => task.UserProfileId);

            entity.HasOne<UserProfile>()
                .WithMany()
                .HasForeignKey(task => task.UserProfileId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PetProfile>(entity =>
        {
            entity.ToTable("pet_profiles", table =>
                table.HasCheckConstraint("CK_pet_profiles_species", "species IN ('dog', 'cat', 'rabbit', 'fox', 'panda', 'dragon')"));

            entity.HasKey(pet => pet.Id);

            entity.Property(pet => pet.Id)
                .HasColumnName("id");

            entity.Property(pet => pet.UserProfileId)
                .HasColumnName("user_profile_id");

            entity.Property(pet => pet.Name)
                .HasColumnName("name")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(pet => pet.Species).HasColumnName("species").HasMaxLength(20).HasDefaultValue("dog");

            entity.Property(pet => pet.Level)
                .HasColumnName("level");

            entity.Property(pet => pet.Experience)
                .HasColumnName("experience");

            entity.Property(pet => pet.TotalExperience)
                .HasColumnName("total_experience");

            entity.Property(pet => pet.CompletedTaskCount)
                .HasColumnName("completed_task_count");

            entity.Property(pet => pet.StreakDays)
                .HasColumnName("streak_days");

            entity.Property(pet => pet.Energy)
                .HasColumnName("energy");

            entity.Property(pet => pet.Mood)
                .HasColumnName("mood")
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(pet => pet.LastCompletedAt)
                .HasColumnName("last_completed_at");

            entity.Property(pet => pet.LegacyLastCompletedAt).HasColumnName("legacy_last_completed_at");
            entity.Property(pet => pet.LegacyStreakDays).HasColumnName("legacy_streak_days");

            entity.Property(pet => pet.CreatedAt)
                .HasColumnName("created_at");

            entity.Property(pet => pet.UpdatedAt)
                .HasColumnName("updated_at");

            entity.Property(pet => pet.Version)
                .IsRowVersion();

            entity.HasIndex(pet => pet.UserProfileId)
                .IsUnique();

            entity.HasOne<UserProfile>()
                .WithOne()
                .HasForeignKey<PetProfile>(pet => pet.UserProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("user_profiles", table =>
            {
                table.HasCheckConstraint("CK_user_profiles_theme", "theme IN ('classic', 'retro', 'light', 'dark', 'forest', 'sunset')");
                table.HasCheckConstraint("CK_user_profiles_layout", "layout IN ('board', 'list', 'compact', 'gallery', 'focus')");
            });

            entity.HasKey(user => user.Id);

            entity.Property(user => user.Id)
                .HasColumnName("id");

            entity.Property(user => user.UserKey)
                .HasColumnName("user_key")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(user => user.DisplayName)
                .HasColumnName("display_name")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(user => user.Theme).HasColumnName("theme").HasMaxLength(20).HasDefaultValue("classic");
            entity.Property(user => user.Layout).HasColumnName("layout").HasMaxLength(20).HasDefaultValue("board");

            entity.Property(user => user.PasswordHash)
                .HasColumnName("password_hash");

            entity.Property(user => user.AcceptedTermsVersion).HasMaxLength(30);
            entity.Property(user => user.AcknowledgedPrivacyVersion).HasMaxLength(30);
            entity.Property(user => user.SessionVersion).HasMaxLength(36);

            entity.Property(user => user.LastLoginAt)
                .HasColumnName("last_login_at");

            entity.Property(user => user.CreatedAt)
                .HasColumnName("created_at");

            entity.Property(user => user.UpdatedAt)
                .HasColumnName("updated_at");

            entity.HasIndex(user => user.UserKey)
                .IsUnique();

            entity.HasIndex(user => user.NormalizedEmail)
                .IsUnique();
        });
    }
}
