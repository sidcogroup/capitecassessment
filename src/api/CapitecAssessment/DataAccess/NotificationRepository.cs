using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Models;

namespace CapitecAssessment.DataAccess;

public class NotificationRepository : INotificationRepository
{
    private readonly ApplicationDbContext _db;

    public NotificationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Notification notification, CancellationToken cancellationToken)
    {
        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
