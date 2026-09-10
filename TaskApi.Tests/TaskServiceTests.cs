using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;
using TaskApi.Services;

namespace TaskApi.Tests;

public class TaskServiceTests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static TaskService CreateService(AppDbContext context)
    {
        var userProfileService = CreateUserProfileService(context);
        var petService = new PetService(context, NullLogger<PetService>.Instance, userProfileService);

        return new TaskService(context, NullLogger<TaskService>.Instance, petService, userProfileService);
    }

    private static PetService CreatePetService(AppDbContext context)
    {
        return new PetService(context, NullLogger<PetService>.Instance, CreateUserProfileService(context));
    }

    private static UserProfileService CreateUserProfileService(AppDbContext context)
    {
        return new UserProfileService(context, NullLogger<UserProfileService>.Instance);
    }

    [Fact]
    public void Create_AddsTask()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        var request = new CreateTaskRequest
        {
            Title = "Test task",
            Description = "Test description",
            IsCompleted = false,
            DueDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            Priority = TaskPriority.High,
            Tags = "portfolio, api"
        };

        var result = service.Create(null, request);

        Assert.True(result.Id > 0);
        Assert.Equal("Test task", result.Title);
        Assert.Equal(TaskPriority.High, result.Priority);
        Assert.Equal("portfolio, api", result.Tags);
        Assert.Equal(request.DueDate, result.DueDate);
        Assert.Single(context.Tasks);
    }

    [Fact]
    public void Create_WhenUserReachedTaskLimit_RejectsAdditionalTask()
    {
        using var context = CreateDbContext();
        var user = CreateUserProfileService(context).GetOrCreateByKey("quota-user");
        var existingTasks = Enumerable.Range(1, 500)
            .Select(index => new TaskItem
            {
                UserProfileId = user.Id,
                Title = $"Task {index}"
            });
        context.Tasks.AddRange(existingTasks);
        context.SaveChanges();
        var service = CreateService(context);

        var action = () => service.Create("quota-user", new CreateTaskRequest
        {
            Title = "One task too many"
        });

        Assert.Throws<TaskLimitExceededException>(action);
        Assert.Equal(500, context.Tasks.Count());
    }

    [Fact]
    public void Update_WhenTaskMovesToDone_AddsPetExperience()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        var createdTask = service.Create(null, new CreateTaskRequest
        {
            Title = "Test task",
            Description = "Test description"
        });

        var updatedTask = service.Update(null, createdTask.Id, new UpdateTaskRequest
        {
            Title = "Test task",
            Description = "Test description",
            Status = TaskItemStatus.Done
        });

        var pet = Assert.Single(context.PetProfiles);

        Assert.NotNull(updatedTask);
        Assert.Equal(TaskItemStatus.Done, updatedTask.Status);
        Assert.Equal(25, pet.TotalExperience);
        Assert.Equal(1, pet.CompletedTaskCount);
    }

    [Fact]
    public void PetService_UpdateProfile_ChangesPetName()
    {
        using var context = CreateDbContext();
        var service = CreatePetService(context);

        var result = service.UpdateProfile(null, new UpdatePetRequest
        {
            Name = "Pochi"
        });

        Assert.Equal("Pochi", result.Name);
        Assert.Equal("Pochi", context.PetProfiles.Single().Name);
    }

    [Fact]
    public void PetService_GetProfile_ReturnsTitleAndAchievements()
    {
        using var context = CreateDbContext();
        var taskService = CreateService(context);
        var petService = CreatePetService(context);

        var createdTask = taskService.Create(null, new CreateTaskRequest
        {
            Title = "Achievement task",
            Description = "Check pet response"
        });

        taskService.Update(null, createdTask.Id, new UpdateTaskRequest
        {
            Title = "Achievement task",
            Description = "Check pet response",
            Status = TaskItemStatus.Done
        });

        var result = petService.GetProfile(null);

        Assert.Equal("見習い相棒", result.Title);
        Assert.Contains("初完了", result.Achievements);
        Assert.True(result.ExperienceProgress > 0);
        Assert.True(result.ExperienceRemaining > 0);
    }

    [Fact]
    public void PetService_GetProfile_DoesNotPenalizeInactiveDays()
    {
        using var context = CreateDbContext();
        var petService = CreatePetService(context);

        petService.UpdateProfile(null, new UpdatePetRequest
        {
            Name = "Pochi"
        });

        var pet = Assert.Single(context.PetProfiles);
        pet.Energy = 80;
        pet.LastCompletedAt = DateTime.UtcNow.AddDays(-4);
        context.SaveChanges();

        var result = petService.GetProfile(null);

        Assert.Equal(80, result.Energy);
        Assert.Equal("Idle", result.Mood);
    }

    [Fact]
    public void Update_WhenDoneTaskMovesBackToDone_ReversesThenRestoresOneReward()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        var createdTask = service.Create(null, new CreateTaskRequest
        {
            Title = "Repeat check",
            Description = "No double reward"
        });

        service.Update(null, createdTask.Id, new UpdateTaskRequest
        {
            Title = "Repeat check",
            Description = "No double reward",
            Status = TaskItemStatus.Done
        });

        service.Update(null, createdTask.Id, new UpdateTaskRequest
        {
            Title = "Repeat check",
            Description = "No double reward",
            Status = TaskItemStatus.Doing
        });

        Assert.Equal(0, context.PetProfiles.Single().TotalExperience);
        Assert.Equal(0, context.PetProfiles.Single().CompletedTaskCount);
        Assert.NotNull(context.CompletionRewards.Single().RevokedAt);

        service.Update(null, createdTask.Id, new UpdateTaskRequest
        {
            Title = "Repeat check",
            Description = "No double reward",
            Status = TaskItemStatus.Done
        });

        var pet = Assert.Single(context.PetProfiles);

        Assert.Equal(25, pet.TotalExperience);
        Assert.Equal(1, pet.CompletedTaskCount);
        Assert.Null(Assert.Single(context.CompletionRewards).RevokedAt);
    }

    [Fact]
    public void GetAll_WhenStatusIsSpecified_ReturnsMatchingStatusOnly()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        service.Create(null, new CreateTaskRequest
        {
            Title = "Todo task",
            Status = TaskItemStatus.Todo
        });

        service.Create(null, new CreateTaskRequest
        {
            Title = "Doing task",
            Status = TaskItemStatus.Doing
        });

        service.Create(null, new CreateTaskRequest
        {
            Title = "Done task",
            Status = TaskItemStatus.Done
        });

        var result = service.GetAll(null, null, TaskItemStatus.Doing, null, null, null, null, 1, 10);

        Assert.Single(result.Items);
        Assert.Equal("Doing task", result.Items[0].Title);
        Assert.Equal(TaskItemStatus.Doing, result.Items[0].Status);
    }

    [Fact]
    public void GetAll_WhenPriorityAndTagAreSpecified_ReturnsMatchingTasksOnly()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        service.Create(null, new CreateTaskRequest
        {
            Title = "Portfolio task",
            Priority = TaskPriority.High,
            Tags = "portfolio, api"
        });

        service.Create(null, new CreateTaskRequest
        {
            Title = "Home task",
            Priority = TaskPriority.Low,
            Tags = "home"
        });

        var result = service.GetAll(null, null, null, TaskPriority.High, "portfolio", null, null, 1, 10);

        Assert.Single(result.Items);
        Assert.Equal("Portfolio task", result.Items[0].Title);
        Assert.Equal(TaskPriority.High, result.Items[0].Priority);
        Assert.Contains("portfolio", result.Items[0].Tags);
    }

    [Fact]
    public void GetAll_WhenUserKeyDiffers_ReturnsOwnTasksOnly()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        service.Create("alice", new CreateTaskRequest
        {
            Title = "Alice task"
        });

        service.Create("bob", new CreateTaskRequest
        {
            Title = "Bob task"
        });

        var aliceTasks = service.GetAll("alice", null, null, null, null, null, null, 1, 10);
        var bobTasks = service.GetAll("bob", null, null, null, null, null, null, 1, 10);

        Assert.Single(aliceTasks.Items);
        Assert.Single(bobTasks.Items);
        Assert.Equal("Alice task", aliceTasks.Items[0].Title);
        Assert.Equal("Bob task", bobTasks.Items[0].Title);
    }

    [Fact]
    public void UserProfileService_ProfileCrud_PreservesIdentityFieldsAndOtherUsers()
    {
        using var context = CreateDbContext();
        var service = CreateUserProfileService(context);
        var original = service.GetOrCreateByKey(" Profile-User ");
        var other = service.GetOrCreateByKey("other-user");
        original.Email = "profile@example.test";
        original.EmailConfirmed = true;
        var securityStamp = original.SecurityStamp;
        var sessionVersion = original.SessionVersion;
        context.SaveChanges();

        var updated = service.UpdateProfile("PROFILE-USER", new UpdateUserProfileRequest
        {
            DisplayName = " Updated Display Name "
        });
        var retrieved = service.GetProfile("profile-user");

        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(original.Id, retrieved.Id);
        Assert.Equal("profile-user", updated.UserKey);
        Assert.Equal("Updated Display Name", retrieved.DisplayName);
        Assert.Equal("profile@example.test", original.Email);
        Assert.True(original.EmailConfirmed);
        Assert.Equal(securityStamp, original.SecurityStamp);
        Assert.Equal(sessionVersion, original.SessionVersion);
        Assert.Equal("PROFILE-USER", original.NormalizedUserName);

        Assert.True(service.DeleteProfile("PROFILE-USER"));
        Assert.False(service.DeleteProfile("profile-user"));
        Assert.Equal(other.Id, Assert.Single(context.UserProfiles).Id);
    }
}
