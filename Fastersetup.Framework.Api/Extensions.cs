/*
 * Copyright 2022 Francesco Cattoni
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * version 3 as published by the Free Software Foundation.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 */

using System.Reflection;
using System.Text;
using Fastersetup.Framework.Api.Attributes.Data;
using Fastersetup.Framework.Api.Data;
using Fastersetup.Framework.Api.Services;
using Fastersetup.Framework.Api.Services.Default;
using Fastersetup.Framework.Api.Services.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Fastersetup.Framework.Api;

public static class Extensions {
	public static IServiceCollection AddDbContextProxy<TContext>(this IServiceCollection services,
		ServiceLifetime lifetime = ServiceLifetime.Scoped) where TContext : DbContext {
		services.TryAdd(new ServiceDescriptor(
			typeof(DbContext),
			p => p.GetService<TContext>()!, lifetime));
		return services;
	}

	public static IServiceCollection AddDbContextProxy(this IServiceCollection services, Type dbContext,
		ServiceLifetime lifetime = ServiceLifetime.Scoped) {
		if (!typeof(DbContext).IsAssignableFrom(dbContext))
			throw new ArgumentException("DbContext implementation type must extend a DbContext implementation");
		services.TryAdd(new ServiceDescriptor(
			typeof(DbContext),
			p => p.GetService(dbContext)!, lifetime));
		return services;
	}

	// TODO: review SessionDomainProvider pattern in ApplicationDbContext to use a service
	/*public static IServiceCollection AddDomainManagement(this IServiceCollection services) {
		services.AddTransient<FilteringService>();
		return services;
	}*/

	public static IServiceCollection AddActiveDomainProvider(this IServiceCollection services) {
		services.AddScoped<ActiveDomainProvider<Domain>>();
		return services;
	}

	public static IServiceCollection AddActiveDomainProvider<TDomain>(this IServiceCollection services)
		where TDomain : Domain {
		services.AddScoped<ActiveDomainProvider<TDomain>>();
		return services;
	}

	public static IServiceCollection AddFastersetupCore(this IServiceCollection services) {
		services
			.AddSingleton<IObjectManipulatorRepository, DefaultObjectManipulatorRepository>()
			.AddTransient<FilteringService>()
			.AddTransient<IObjectUtils, ObjectUtils>()
			.AddHttpContextAccessor();
		return services;
	}

	public static IServiceCollection AddFastersetupCoreMySqlDefaults<TDbContext>(this IServiceCollection services)
		where TDbContext : DbContext {
		services
			.AddFastersetupCore()
			.AddDbContextProxy<TDbContext>()
			.AddActiveDomainProvider();
		return services;
	}

	public static DbContextOptionsBuilder EnableOrderByMemoryPatch(this DbContextOptionsBuilder builder,
		ServerVersion version) {
		if (version.Version < new Version(8, 0))
			builder.AddInterceptors(new RemoveLastOrderByInterceptor());
		return builder;
	}

	/// <summary>
	/// Transforms a column name to the equivalent database reference
	/// </summary>
	/// <returns>The transformed <paramref name="columnName"/> or null if no transformation is required</returns>
	public delegate string? ColumnNameTransformer(string? columnName, IReadOnlyProperty property);

	public static ModelBuilder AddDefaultDomain(this ModelBuilder builder) {
		builder.Entity<Domain>().ToTable("domain");
		return builder;
	}

	public static ModelBuilder RegisterExtensions(this ModelBuilder builder,
		ColumnNameTransformer? columnNameTransformer = null,
		bool guidShortening = true,
		bool timeSpanConversion = true,
		bool timestampSupport = true,
		bool timezonePatch = true) {
		foreach (var type in builder.Model.GetEntityTypes()) {
			var entity = builder.Entity(type.Name);
			var store = StoreObjectIdentifier.Create(entity.Metadata, StoreObjectType.Table)!.Value;
			foreach (var property in type.GetProperties()) {
				var pb = entity.Property(property.Name);
				// Column name mangling
				var name = (columnNameTransformer ?? MySqlTransformColumnName)(property.GetColumnName(store), property);
				if (name != null)
					pb.HasColumnName(name);
				// Guid shortening
				if (guidShortening)
					if (property.ClrType == typeof(Guid))
						pb.HasConversion(new ValueConverter<Guid, string>(
							guid => guid.ToString("N"),
							guid => Guid.Parse(guid),
							new ConverterMappingHints(32,
								valueGeneratorFactory: (_, _) => new GuidValueGenerator())));
					else if (property.ClrType == typeof(Guid?))
						pb.HasConversion(new ValueConverter<Guid?, string?>(
							guid => guid.HasValue ? guid.Value.ToString("N") : null,
							guid => string.IsNullOrEmpty(guid) ? null : Guid.Parse(guid),
							new ConverterMappingHints(32,
								valueGeneratorFactory: (_, _) => new GuidValueGenerator())));

				var member = (MemberInfo?) property.PropertyInfo ?? property.FieldInfo;

				// TimeSpan conversion
				if (timeSpanConversion) {
					var precision = member?.GetCustomAttribute<TimeSpanPrecisionAttribute>();
					if (precision == null) {
						if (property.ClrType == typeof(TimeSpan))
							pb.HasConversion<uint>();
						else if (property.ClrType == typeof(TimeSpan?))
							pb.HasConversion(new ValueConverter<TimeSpan?, uint?>(
								span => span.HasValue ? (uint?) span.Value.Ticks : null,
								span => span.HasValue ? new TimeSpan(span.Value) : null));
					} else {
						long unit;
						switch (precision.Precision) {
							case TimeSpanPrecision.Tick:
								unit = 1;
								break;
							case TimeSpanPrecision.Microsecond:
								unit = TimeSpan.TicksPerMillisecond / 1000;
								break;
							case TimeSpanPrecision.Millisecond:
								unit = TimeSpan.TicksPerMillisecond;
								break;
							case TimeSpanPrecision.Second:
								unit = TimeSpan.TicksPerSecond;
								break;
							case TimeSpanPrecision.Minute:
								unit = TimeSpan.TicksPerMinute;
								break;
							case TimeSpanPrecision.Hour:
								unit = TimeSpan.TicksPerHour;
								break;
							case TimeSpanPrecision.Day:
								unit = TimeSpan.TicksPerDay;
								break;
							default:
								throw new ArgumentOutOfRangeException("precision",
									$"Invalid {nameof(TimeSpanPrecision)} defined for property {property.DeclaringEntityType.Name}.{property.Name}");
						}

						if (precision.UseShorterUnit) {
							if (property.ClrType == typeof(TimeSpan))
								pb.HasConversion(new ValueConverter<TimeSpan, ushort>(
									span => (ushort) (span.Ticks / unit),
									span => new TimeSpan(span * unit)));
							else if (property.ClrType == typeof(TimeSpan?))
								pb.HasConversion(new ValueConverter<TimeSpan?, ushort?>(
									span => span.HasValue ? (ushort?) (span.Value.Ticks / unit) : null,
									span => span.HasValue ? new TimeSpan(span.Value * unit) : null));
						} else {
							if (property.ClrType == typeof(TimeSpan))
								pb.HasConversion(new ValueConverter<TimeSpan, uint>(
									span => (uint) (span.Ticks / unit),
									span => new TimeSpan(span * unit)));
							else if (property.ClrType == typeof(TimeSpan?))
								pb.HasConversion(new ValueConverter<TimeSpan?, uint?>(
									span => span.HasValue ? (uint?) (span.Value.Ticks / unit) : null,
									span => span.HasValue ? new TimeSpan(span.Value * unit) : null));
						}
					}
				}

				// Timestamp support
				if (timestampSupport) {
					var tsAttr = member?.GetCustomAttribute<RowTimestampAttribute>();
					if (tsAttr != null)
						if (tsAttr.SetOnUpdate)
							pb.ValueGeneratedOnAddOrUpdate();
						else
							pb.ValueGeneratedOnAdd();
				}

				// Timezone patch
				if (timezonePatch)
					if (property.ClrType == typeof(DateTime))
						entity.Property<DateTime>(property.Name)
							.HasConversion(
								e => e.ToUniversalTime(),
								e => DateTime.SpecifyKind(e, DateTimeKind.Utc));
					else if (property.ClrType == typeof(DateTime?))
						entity.Property<DateTime?>(property.Name)
							.HasConversion(
								e => e.HasValue
									? e.Value.ToUniversalTime()
									: (DateTime?) null,
								e => e.HasValue
									? DateTime.SpecifyKind(e.Value, DateTimeKind.Utc)
									: null);
			}
		}

		return builder;
	}

	private static string? MySqlTransformColumnName(string? columnName, IReadOnlyProperty property) {
		if (columnName == null)
			return null;
		var sb = new StringBuilder(columnName);
		for (var i = 0; i < sb.Length; i++) {
			var c = sb[i];
			if (!char.IsUpper(c))
				continue;
			if (i == 0)
				sb[i] = char.ToLower(c);
			else
				sb.Insert(i, '_')[i + 1] = char.ToLower(c);
		}

		return sb.ToString();
	}
}