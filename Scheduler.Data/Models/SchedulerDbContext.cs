using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Models
{
    public class SchedulerDbContext : DbContext
    {
        public SchedulerDbContext(DbContextOptions<SchedulerDbContext> options)
            : base(options)
        {
        }

        public DbSet<Steward> Stewards { get; set; }
        public DbSet<MonthlyHours> MonthlyHours { get; set; }
        public DbSet<StewardLicense> StewardLicenses { get; set; }
        public DbSet<StewardLanguage> StewardLanguages { get; set; }
        public DbSet<Language> Languages { get; set; }
        public DbSet<Flight> Flights { get; set; }
        public DbSet<Feedback> Feedbacks { get; set; }
        public DbSet<Assignment> Assignments { get; set; }
        public DbSet<AircraftType> AircraftTypes { get; set; }
        public DbSet<AircraftLicense> AircraftLicenses { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure composite keys
            modelBuilder.Entity<StewardLicense>()
                .HasKey(sl => new { sl.StewardId, sl.LicenseId });

            modelBuilder.Entity<StewardLanguage>()
                .HasKey(sl => new { sl.StewardId, sl.LanguageId });

            // Configure table names to match your existing database
            modelBuilder.Entity<Steward>().ToTable("stewards");
            modelBuilder.Entity<MonthlyHours>().ToTable("monthlyhours");
            modelBuilder.Entity<StewardLicense>().ToTable("stewardlicenses");
            modelBuilder.Entity<StewardLanguage>().ToTable("stewardlanguages");
            modelBuilder.Entity<Language>().ToTable("languages");
            modelBuilder.Entity<Flight>().ToTable("flights");
            modelBuilder.Entity<Feedback>().ToTable("feedback");
            modelBuilder.Entity<Assignment>().ToTable("assignments");
            modelBuilder.Entity<AircraftType>().ToTable("aircrafttypes");
            modelBuilder.Entity<AircraftLicense>().ToTable("aircraftlicenses");

            // Configure column names
            modelBuilder.Entity<Steward>(entity =>
            {
                entity.Property(e => e.StewardId).HasColumnName("steward_id");
                entity.Property(e => e.FirstName).HasColumnName("first_name");
                entity.Property(e => e.LastName).HasColumnName("last_name");
                entity.Property(e => e.RoleString).HasColumnName("role"); // Map to the role column
                entity.Property(e => e.IsSenior).HasColumnName("is_senior");
                entity.Property(e => e.JoiningDate).HasColumnName("joining_date");
                entity.Property(e => e.LastFlightEndTime).HasColumnName("last_flight_end_time");
            });

            modelBuilder.Entity<MonthlyHours>(entity =>
            {
                entity.Property(e => e.RecordId).HasColumnName("record_id");
                entity.Property(e => e.StewardId).HasColumnName("steward_id");
                entity.Property(e => e.Year).HasColumnName("year");
                entity.Property(e => e.Month).HasColumnName("month");
                entity.Property(e => e.HoursWorked).HasColumnName("hours_worked");
            });

            modelBuilder.Entity<StewardLicense>(entity =>
            {
                entity.Property(e => e.StewardId).HasColumnName("steward_id");
                entity.Property(e => e.LicenseId).HasColumnName("license_id");
            });

            modelBuilder.Entity<StewardLanguage>(entity =>
            {
                entity.Property(e => e.StewardId).HasColumnName("steward_id");
                entity.Property(e => e.LanguageId).HasColumnName("language_id");
            });

            modelBuilder.Entity<Language>(entity =>
            {
                entity.Property(e => e.LanguageId).HasColumnName("language_id");
                entity.Property(e => e.LanguageName).HasColumnName("language_name");
            });

            modelBuilder.Entity<Flight>(entity =>
            {
                entity.Property(e => e.FlightId).HasColumnName("flight_id");
                entity.Property(e => e.FlightNumber).HasColumnName("flight_number");
                entity.Property(e => e.DepartureTime).HasColumnName("departure_time");
                entity.Property(e => e.ArrivalTime).HasColumnName("arrival_time");
                entity.Property(e => e.AircraftType).HasColumnName("aircraft_type");
                entity.Property(e => e.Destination).HasColumnName("destination");
                entity.Property(e => e.RequiredLanguageId).HasColumnName("required_language_id");
                entity.Property(e => e.FlightTime).HasColumnName("flight_time");
                entity.Property(e => e.Priority).HasColumnName("priority");
            });

            modelBuilder.Entity<Feedback>(entity =>
            {
                entity.Property(e => e.FeedbackId).HasColumnName("feedback_id");
                entity.Property(e => e.StewardId).HasColumnName("steward_id");
                entity.Property(e => e.FeedbackType).HasColumnName("feedback_type");
                entity.Property(e => e.FeedbackText).HasColumnName("feedback_text");
            });

            modelBuilder.Entity<Assignment>(entity =>
            {
                entity.Property(e => e.AssignmentId).HasColumnName("assignment_id");
                entity.Property(e => e.StewardId).HasColumnName("steward_id");
                entity.Property(e => e.FlightId).HasColumnName("flight_id");
            });

            modelBuilder.Entity<AircraftType>(entity =>
            {
                entity.Property(e => e.AircraftTypeId).HasColumnName("aircraft_type");
                entity.Property(e => e.BusinessClassCrew).HasColumnName("business_class_crew");
                entity.Property(e => e.EconomyClassCrew).HasColumnName("economy_class_crew");
            });

            modelBuilder.Entity<AircraftLicense>(entity =>
            {
                entity.Property(e => e.LicenseId).HasColumnName("license_id");
                entity.Property(e => e.AircraftTypeId).HasColumnName("aircraft_type");
            });
        }
    }
}