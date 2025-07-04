using B2a.DbTula.Core.Abstractions;
using B2A.DbTula.Core.Models;

namespace B2A.DbTula.Core.Abstractions; 

public interface ISchemaComparer
{
    Task<IList<ComparisonResult>> CompareAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Action<string>? progressReporter = null,
        bool runForTest = false
    );
}