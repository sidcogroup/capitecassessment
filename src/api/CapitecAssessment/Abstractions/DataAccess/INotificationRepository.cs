using CapitecAssessment.Models;

namespace CapitecAssessment.Abstractions.DataAccess;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken cancellationToken);
}
