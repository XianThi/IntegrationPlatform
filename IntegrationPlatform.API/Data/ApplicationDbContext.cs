using IntegrationPlatform.API.Models;
using Microsoft.EntityFrameworkCore;

namespace IntegrationPlatform.API.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Node> Nodes { get; set; }
        public DbSet<WorkflowDefinition> WorkflowDefinitions { get; set; }
        public DbSet<WorkflowStep> WorkflowSteps { get; set; }
        public DbSet<WorkflowExecution> WorkflowExecutions { get; set; }
        public DbSet<StepExecution> StepExecutions { get; set; }
        public DbSet<SystemLog> SystemLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Node indexes
            modelBuilder.Entity<Node>()
                .HasIndex(n => n.NodeName)
                .IsUnique();

            modelBuilder.Entity<Node>()
                .HasIndex(n => n.Status);

            modelBuilder.Entity<Node>()
                .HasIndex(n => n.LastHeartbeat);

            // Workflow indexes
            modelBuilder.Entity<WorkflowDefinition>()
                .HasIndex(w => w.Status);

            modelBuilder.Entity<WorkflowDefinition>()
                .HasIndex(w => w.AssignedNodeId);

            modelBuilder.Entity<WorkflowDefinition>()
                .HasIndex(w => w.IsActive);

            // WorkflowExecution indexes
            modelBuilder.Entity<WorkflowExecution>()
                .HasIndex(e => e.Status);

            modelBuilder.Entity<WorkflowExecution>()
                .HasIndex(e => e.StartedAt);

            // SystemLog indexes
            modelBuilder.Entity<SystemLog>()
                .HasIndex(l => l.Timestamp);

            modelBuilder.Entity<SystemLog>()
                .HasIndex(l => l.Level);

            modelBuilder.Entity<SystemLog>()
                .HasIndex(l => new { l.Timestamp, l.Level });

            // JSONB column configurations
            modelBuilder.Entity<Node>()
                .Property(n => n.SupportedAdapters)
                .HasColumnType("jsonb");

            modelBuilder.Entity<Node>()
                .Property(n => n.Metrics)
                .HasColumnType("jsonb");

            modelBuilder.Entity<WorkflowDefinition>()
                .Property(w => w.Steps)
                .HasColumnType("jsonb");

            modelBuilder.Entity<WorkflowDefinition>()
                .Property(w => w.GlobalVariables)
                .HasColumnType("jsonb");

            modelBuilder.Entity<WorkflowExecution>()
                .Property(e => e.StepExecutions)
                .HasColumnType("jsonb");

            modelBuilder.Entity<StepExecution>()
                .Property(s => s.OutputPreview)
                .HasColumnType("jsonb");

            modelBuilder.Entity<StepExecution>()
                .Property(s => s.ExecutionMetrics)
                .HasColumnType("jsonb");

            modelBuilder.Entity<SystemLog>()
                .Property(l => l.Properties)
                .HasColumnType("jsonb");

            // Relationships
            modelBuilder.Entity<WorkflowDefinition>()
                .HasOne(w => w.AssignedNode)
                .WithMany(n => n.AssignedWorkflows)
                .HasForeignKey(w => w.AssignedNodeId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<WorkflowExecution>()
                .HasOne(e => e.WorkflowDefinition)
                .WithMany(w => w.Executions)
                .HasForeignKey(e => e.WorkflowDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<WorkflowExecution>()
                .HasOne(e => e.ExecutedBy)
                .WithMany()
                .HasForeignKey(e => e.NodeId)
                .OnDelete(DeleteBehavior.SetNull);

            //modelBuilder.Entity<StepExecution>()
            //    .HasOne(s => s.WorkflowExecution)
            //    .WithMany(e => e.StepExecutions)
            //    .HasForeignKey(s => s.WorkflowExecutionId)
            //    .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
