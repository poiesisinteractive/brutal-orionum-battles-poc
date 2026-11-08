// Visualiseur de débug : la sim tourne en direct dans une fenêtre OpenGL, pilotée en ImGui.
//
//   dotnet run --project src/OrionumFleet.Viewer -c Release
//
// Ordre d'initialisation repris de Brutal Arena : GLFW -> ImGui, libérés dans l'ordre inverse.

using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using OrionumFleet.Viewer;

if (!Glfw.Init())
{
    var errorCode = Glfw.GetError(out var errorMessage);
    throw new InvalidOperationException($"Échec de l'initialisation de GLFW : {errorCode}, {errorMessage}");
}

var imGuiContext = ImGui.CreateContext();
try
{
    // Le visualiseur possède la fenêtre et les backends ImGui : il est libéré avant le contexte et GLFW.
    using var viewer = new Viewer(args);
    viewer.Run();
}
finally
{
    ImGui.DestroyContext(imGuiContext);
    Glfw.Terminate();
}
