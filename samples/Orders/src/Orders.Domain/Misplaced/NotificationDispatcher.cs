// DELIBERATE VIOLATION SOURCE.
//
// This file sits in the Orders.Domain *project* but declares an Orders.Application *namespace*.
// Tangent binds layers to namespaces, not to projects, so any Domain type that touches
// INotificationDispatcher lands in the Domain -> Application layer violation (TNG0202) even though
// no project reference exists. This is the shape the mistake actually takes in real codebases.

namespace Orders.Application.Notifications;

public interface INotificationDispatcher
{
    void Dispatch(string message);
}
