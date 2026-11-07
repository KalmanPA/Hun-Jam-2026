using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Added by an EnemySpawner: the enemy falls straight down from the spawner with its AI paused,
/// lands on the floor below, then its NavMeshAgent and scripts are switched back on and it is alerted.
/// Works for any enemy prefab (scripts on the root are paused, an "Alert" message is sent on landing).
/// </summary>
public class EnemyDrop : MonoBehaviour
{
    private static readonly RaycastHit[] Hits = new RaycastHit[32];

    private Behaviour[] paused;
    private NavMeshAgent agent;
    private RobotEnemy robot;
    private float gravity;
    private float fallSpeed;
    private float groundY;
    private bool hasGround;

    public bool Landed { get; private set; }

    /// <summary>Call on a freshly spawned enemy before it becomes active.</summary>
    public static EnemyDrop Begin(GameObject enemy, float gravity)
    {
        var list = new List<Behaviour>();
        foreach (Behaviour b in enemy.GetComponents<Behaviour>())
        {
            if (!b.enabled || !(b is MonoBehaviour || b is NavMeshAgent)) continue;
            b.enabled = false; // the agent would snap the enemy down to the NavMesh straight away
            list.Add(b);
        }

        EnemyDrop drop = enemy.AddComponent<EnemyDrop>();
        drop.paused = list.ToArray();
        drop.gravity = gravity;
        drop.agent = enemy.GetComponent<NavMeshAgent>();
        drop.robot = enemy.GetComponent<RobotEnemy>();
        return drop;
    }

    private void Update()
    {
        // Shot dead in mid air: the robot's own death physics take over.
        if (robot != null && robot.IsDead) { Landed = true; Destroy(this); return; }

        if (!hasGround) FindGround();

        fallSpeed += gravity * Time.deltaTime;
        Vector3 p = transform.position;
        p.y -= fallSpeed * Time.deltaTime;
        if (p.y <= groundY)
        {
            p.y = groundY;
            transform.position = p;
            Land();
            return;
        }
        transform.position = p;
    }

    private void FindGround()
    {
        hasGround = true;
        groundY = transform.position.y - 60f; // nothing below: give up after a long fall

        int count = Physics.RaycastNonAlloc(transform.position + Vector3.up * 0.2f, Vector3.down, Hits, 200f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider c = Hits[i].collider;
            if (c.transform.IsChildOf(transform)) continue;
            // Land on the floor, not on the heads of other enemies or the player.
            if (c.GetComponentInParent<NavMeshAgent>() != null || c.GetComponentInParent<CharacterController>() != null) continue;
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
            if (Hits[i].distance < best)
            {
                best = Hits[i].distance;
                groundY = Hits[i].point.y;
            }
        }
    }

    private void Land()
    {
        if (agent != null && NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 3f, NavMesh.AllAreas))
            transform.position = navHit.position;

        foreach (Behaviour b in paused)
            if (b != null) b.enabled = true;
        if (agent != null && agent.enabled) agent.Warp(transform.position);

        SendMessage("Alert", SendMessageOptions.DontRequireReceiver);
        Landed = true;
        Destroy(this);
    }
}
