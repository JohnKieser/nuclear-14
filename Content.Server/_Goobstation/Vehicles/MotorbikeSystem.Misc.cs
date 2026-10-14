using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Vehicles;


namespace Content.Server.Vehicles;


public partial class MotorbikeSystem
{
    private bool PopUpFails( Solution fuelSolution, Entity<MotorbikeComponent> bike,  EntityUid user)
    {

        var availableFuel = fuelSolution.GetTotalPrototypeQuantity(bike.Comp.FuelReagent);
        if (availableFuel <= FixedPoint2.Zero)
        {
            _popup.PopupEntity("The container has no welding fuel.", bike, user);
            return true;
        }

        if (fuelSolution.Volume != availableFuel)
        {
            _popup.PopupEntity("The motorbike fuel tank rejects contaminated fuel.", bike, user);
            return true;
        }

        if (fuelSolution.AvailableVolume <= FixedPoint2.Zero)
        {
            _popup.PopupEntity("The motorbike fuel tank is already full.", bike, user);
            return true;
        }
        return false;
    }
}
