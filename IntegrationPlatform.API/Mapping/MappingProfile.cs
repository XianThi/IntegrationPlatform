using AutoMapper;
using IntegrationPlatform.API.Models;
using IntegrationPlatform.Common.DTOs;

namespace IntegrationPlatform.API.Mapping
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // Node mappings
            CreateMap<Node, NodeDto>()
                .ForMember(dest => dest.IpAddress, opt => opt.MapFrom(src => src.IpAddress))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status))
                .ForMember(dest => dest.LastHeartbeat, opt => opt.MapFrom(src => src.LastHeartbeat))
                .ForMember(dest => dest.RegisteredAt, opt => opt.MapFrom(src => src.RegisteredAt))
                .ForMember(dest => dest.Metrics, opt => opt.MapFrom(src => src.Metrics));

            // Workflow mappings
            CreateMap<WorkflowDefinition, WorkflowDefinitionDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
                .ForMember(dest => dest.AssignedNodeId, opt => opt.MapFrom(src => src.AssignedNodeId))
                .ForMember(dest => dest.StartTime, opt => opt.MapFrom(src => src.StartTime))
                .ForMember(dest => dest.EndTime, opt => opt.MapFrom(src => src.EndTime))
                .ForMember(dest => dest.IntervalSeconds, opt => opt.MapFrom(src => src.IntervalSeconds))
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.IsActive))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status))
                .ForMember(dest => dest.Steps, opt => opt.MapFrom(src => src.Steps))
                .ForMember(dest => dest.GlobalVariables, opt => opt.MapFrom(src => src.GlobalVariables))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.CreatedAt))
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => src.UpdatedAt));

            CreateMap<WorkflowDefinitionDto, WorkflowDefinition>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.AssignedNode, opt => opt.Ignore())
                .ForMember(dest => dest.Executions, opt => opt.Ignore());

            // WorkflowStep mappings
            CreateMap<WorkflowStep, WorkflowStepDto>().ReverseMap();

            // WorkflowExecution mappings
            CreateMap<WorkflowExecution, WorkflowExecutionDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.WorkflowId, opt => opt.MapFrom(src => src.WorkflowDefinitionId))
                .ForMember(dest => dest.StartedAt, opt => opt.MapFrom(src => src.StartedAt))
                .ForMember(dest => dest.CompletedAt, opt => opt.MapFrom(src => src.CompletedAt))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status))
                .ForMember(dest => dest.ErrorMessage, opt => opt.MapFrom(src => src.ErrorMessage))
                .ForMember(dest => dest.StepExecutions, opt => opt.MapFrom(src => src.StepExecutions));

            CreateMap<WorkflowExecutionDto, WorkflowExecution>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.WorkflowDefinitionId, opt => opt.MapFrom(src => src.WorkflowId))
                .ForMember(dest => dest.WorkflowDefinition, opt => opt.Ignore())
                .ForMember(dest => dest.ExecutedBy, opt => opt.Ignore());

            // StepExecution mappings
            CreateMap<StepExecution, StepExecutionDto>().ReverseMap();
        }
    }
}
