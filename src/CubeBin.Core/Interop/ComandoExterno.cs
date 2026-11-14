using System.ComponentModel;
using System.Diagnostics;

namespace CubeBin.Core.Interop;

/// <summary>
/// Roda um programa do sistema (gdbus, secret-tool, notify-send...) e devolve o código de saída e o
/// que ele escreveu. Usado só no Linux, onde essas ferramentas fazem o papel das APIs do Windows.
/// Nunca lança exceção: se o programa não existir ou demorar demais, devolve null.
/// </summary>
public static class ComandoExterno
{
    public record Resultado(int Codigo, string Saida);

    public static Resultado? Executar(string programa, IEnumerable<string> argumentos,
        string? entrada = null, TimeSpan? limite = null)
    {
        try
        {
            var info = new ProcessStartInfo(programa)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = entrada is not null,
                CreateNoWindow = true,
            };
            foreach (var argumento in argumentos)
                info.ArgumentList.Add(argumento);

            using var processo = Process.Start(info);
            if (processo is null) return null;

            if (entrada is not null)
            {
                processo.StandardInput.Write(entrada);
                processo.StandardInput.Close();
            }

            var leitura = processo.StandardOutput.ReadToEndAsync();
            _ = processo.StandardError.ReadToEndAsync();

            if (!processo.WaitForExit((int)(limite ?? TimeSpan.FromSeconds(10)).TotalMilliseconds))
            {
                try { processo.Kill(entireProcessTree: true); } catch { /* já terminou */ }
                return null;
            }

            return new Resultado(processo.ExitCode, leitura.GetAwaiter().GetResult());
        }
        catch (Win32Exception)
        {
            // O programa não está instalado.
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Dispara um programa sem esperar (ex.: xdg-open). Devolve false se nem conseguiu abrir.</summary>
    public static bool Abrir(string programa, params string[] argumentos)
    {
        try
        {
            var info = new ProcessStartInfo(programa) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argumento in argumentos)
                info.ArgumentList.Add(argumento);
            using var processo = Process.Start(info);
            return processo is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
