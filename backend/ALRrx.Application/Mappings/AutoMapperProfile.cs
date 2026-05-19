using ALRrx.Application.DTOs;
using ALRrx.Domain.Entities;
using AutoMapper;

namespace ALRrx.Application.Mappings;

public sealed class AutoMapperProfile : Profile
{
    public AutoMapperProfile()
    {
        CreateMap<DashboardMetric, MetricCardDto>();

        CreateMap<QueryDefinition, QueryDefinitionDto>();

        CreateMap<ReportResult, ReportDto>()
            .ForMember(dest => dest.TimeRangeStart, opt => opt.MapFrom(src => src.TimeRange!.Start))
            .ForMember(dest => dest.TimeRangeEnd, opt => opt.MapFrom(src => src.TimeRange!.End));
    }
}
