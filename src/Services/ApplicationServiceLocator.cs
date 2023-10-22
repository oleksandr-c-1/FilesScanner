using Microsoft.Extensions.DependencyInjection;
using System;

namespace FilesScanner.Services;

public static class ApplicationServiceLocator {
    public static IServiceProvider Services { get; set; }

    public static T GetService<T>() {
        return Services.GetRequiredService<T>();
    }
}