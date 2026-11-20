using Brutal.Concurrency.Jobs;

namespace OrionumFleet.Sim;

/// <summary>
/// Passes parallèles de la sim sur le JobScheduler de BRUTAL. Une passe découpe [0, n) en tranches contiguës ;
/// le corps ne doit écrire que dans les entrées de sa tranche (et lire ce que la passe ne modifie pas). Le
/// résultat ne dépend donc ni du découpage ni du nombre de cœurs : même graine, même bataille.
/// </summary>
public static class SimJobs
{
    /// <summary>En dessous, la passe tourne sur le thread appelant (le coût de répartition l'emporterait).</summary>
    public const int ParallelFrom = 1024;

    sealed class RangeJob : IJob
    {
        public Action<int, int> Body = null!;
        public int Start;
        public int End;

        public void Run() => Body(Start, End);
    }

    static readonly object Gate = new();
    static JobScheduler? _scheduler;
    static RangeJob[] _jobs = [];

    /// <summary>Nombre de tranches (et de runners) : un par cœur logique, un minimum de 1.</summary>
    public static int Workers { get; } = Math.Max(1, Environment.ProcessorCount);

    /// <summary>Faux : tout tourne sur le thread appelant (comparaisons et débogage).</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Exécute <paramref name="body"/>(début, fin) sur [0, <paramref name="count"/>), en parallèle si la passe est assez grande.</summary>
    public static void For(int count, Action<int, int> body) => For(count, ParallelFrom, body);

    /// <param name="parallelFrom">Taille à partir de laquelle la passe se répartit sur les cœurs.</param>
    public static void For(int count, int parallelFrom, Action<int, int> body)
    {
        if (count <= 0)
            return;
        if (!Enabled || count < parallelFrom || Workers == 1)
        {
            body(0, count);
            return;
        }

        // Le scheduler est partagé : une seule passe parallèle à la fois (les mondes clonés de l'analyse
        // tactique tourneront eux-mêmes sur des threads distincts, en séquentiel).
        lock (Gate)
        {
            _scheduler ??= new JobScheduler((uint)Workers, "Sim");
            if (_jobs.Length < Workers)
            {
                _jobs = new RangeJob[Workers];
                for (var i = 0; i < _jobs.Length; i++)
                    _jobs[i] = new RangeJob();
            }
            var chunk = (count + Workers - 1) / Workers;
            for (var j = 0; j < Workers; j++)
            {
                var start = j * chunk;
                var end = Math.Min(start + chunk, count);
                if (start >= end)
                    break;
                var job = _jobs[j];
                (job.Body, job.Start, job.End) = (body, start, end);
                _scheduler.Queue(job, end - start);
            }
            _scheduler.ExecuteJobs();
            _scheduler.Wait();
        }
    }
}
