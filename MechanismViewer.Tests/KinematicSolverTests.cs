using MechanismViewer.Catalog;
using MechanismViewer.Kinematics;

namespace MechanismViewer.Tests;

public class KinematicSolverTests
{
    private static PartSpec Revolute(string id, int? teeth = null, double pitchMm = 10) => new()
    {
        Id = id,
        Stl = $"{id}.stl",
        Joint = new JointSpec { Type = "revolute" },
        Gear = teeth is { } z ? new GearSpec { Teeth = z, PitchRadiusMm = pitchMm, RootRadiusMm = pitchMm - 1, ThicknessMm = 5 } : null
    };

    private static PartSpec Prismatic(string id) => new()
    {
        Id = id,
        Stl = $"{id}.stl",
        Joint = new JointSpec { Type = "prismatic", Axis = [1, 0, 0] },
        MassKg = 0.01
    };

    private static MechanismDefinition Mechanism(IReadOnlyList<PartSpec> parts, IReadOnlyList<CouplingSpec> couplings) => new()
    {
        Id = "test",
        Name = "test",
        Motion = new MotionSpec { Input = "rotation", Output = "linear" },
        Driver = new DriverSpec { Type = "motor", Part = parts[0].Id },
        Parts = parts,
        Couplings = couplings
    };

    [Fact]
    public void Chain_MultipliesTransferFunctions()
    {
        // Мотор → шестерня 12 → колесо 36 (−1/3) → его вал с шестернёй 10 мм → рейка.
        MechanismDefinition m = Mechanism(
            [Revolute("pinion", 12, 6), Revolute("wheel", 36, 18), Revolute("output"), Prismatic("rack")],
            [
                new CouplingSpec { Type = "gear", From = "pinion", To = "wheel" },
                new CouplingSpec { Type = "gear", From = "wheel", To = "output", Ratio = 1.0 },
                new CouplingSpec { Type = "rack", From = "output", To = "rack", RadiusMm = 10 }
            ]);

        var solver = new KinematicSolver(m);
        PartState[] state = solver.Solve(3.0);

        Assert.Equal(["pinion", "wheel", "output", "rack"], solver.Parts.Select(p => p.Id));
        Assert.Equal(-1.0, state[solver.IndexOf("wheel")].Q, 12);
        Assert.Equal(-1.0 / 3.0, state[solver.IndexOf("output")].K, 12);
        Assert.Equal(-0.01, state[solver.IndexOf("rack")].Q, 12);
        Assert.Equal(-0.01 / 3.0, state[solver.IndexOf("rack")].K, 12);
    }

    [Fact]
    public void UnconnectedMovingPart_IsRejected()
    {
        MechanismDefinition m = Mechanism([Revolute("a", 10), Revolute("b", 10), Revolute("loose")],
            [new CouplingSpec { Type = "gear", From = "a", To = "b" }]);

        var error = Assert.Throws<InvalidDataException>(() => new KinematicSolver(m));
        Assert.Contains("loose", error.Message);
    }

    [Fact]
    public void ClosedLoop_IsRejected()
    {
        MechanismDefinition m = Mechanism([Revolute("a", 10), Revolute("b", 10), Revolute("c", 10)],
            [
                new CouplingSpec { Type = "gear", From = "a", To = "b" },
                new CouplingSpec { Type = "gear", From = "a", To = "c" },
                new CouplingSpec { Type = "gear", From = "b", To = "c" }
            ]);

        Assert.Throws<InvalidDataException>(() => new KinematicSolver(m));
    }

    [Fact]
    public void CouplingWithWrongJointTypes_IsRejected()
    {
        MechanismDefinition m = Mechanism([Revolute("a", 10), Prismatic("b")],
            [new CouplingSpec { Type = "gear", From = "a", To = "b", Ratio = 1 }]);

        Assert.Throws<InvalidDataException>(() => new KinematicSolver(m));
    }
}
