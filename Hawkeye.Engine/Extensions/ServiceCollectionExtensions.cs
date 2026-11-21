using Hawkeye.Engine.PacketProcessing;
using Hawkeye.Engine.PacketProcessing.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hawkeye.Engine.Extensions
{
    public static class ServiceCollectionExtensions
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddPacketProcessingRouting()
            {
                var assembly = typeof(PacketProcessor).Assembly;

                var processorTypes = assembly.GetTypes()
                    .Where(t =>
                        t is { IsClass: true, IsAbstract: false } &&
                        t.Namespace is not null &&
                        t.Namespace.Contains(".PacketProcessing.Processors"))
                    .ToArray();

                foreach (var processorType in processorTypes)
                {
                    services.AddSingleton(processorType);
                }

                foreach (var opcodeProcessorType in processorTypes.Where(t => typeof(IOpcodeProcessor).IsAssignableFrom(t)))
                {
                    services.AddSingleton(typeof(IOpcodeProcessor),
                        sp => (IOpcodeProcessor)sp.GetRequiredService(opcodeProcessorType));
                }

                services.AddSingleton<PacketProcessor>();
                services.AddSingleton<OpcodeProcessorRegistry>();
                services.AddSingleton<PacketDispatchService>();

                return services;
            }
        }
    }
}
