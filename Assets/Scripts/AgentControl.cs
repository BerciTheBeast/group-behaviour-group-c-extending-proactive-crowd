using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum AgentBehaviourType
{
    Default,
    GapSeeking,
    Following,
    StopAndGo,
    Overtaking
}

public class AgentControl : MonoBehaviour
{
    private GridComponent gridComponent;
    private Grid grid;
    private NavMeshAgent agent;
    public Vector3 startingPosition;
    public Vector3 destination;
    public SpawnPoint origin;
    private Animator animator;

    public bool respawn = true;
    [Min(1)]
    public int seeds = 3;
    [Min(1)]
    public int gapSearchArea = 5;
    [Min(1.0f)]
    public float visionRadius = 2.5f;
    public float visionAngle = 120.0f;
    public float deviationAngle = 166.0f;
    [Min(0f)]
    public float destinationTresholdAngle = 78.0f;
    public AgentBehaviourType behaviour = AgentBehaviourType.Default;
    public bool isGapSeeker = false;
    public bool isFollower = false;

    private float seekingStart;
    private float seekingDuration;

    public GameObject followedBy = null;
    public GameObject followingTarget = null;
    public float followingStart;
    public float followingDuration;

    // Stop & Go behaviour variables.
    public float stoppingDistance = 0.5f;
    public float stopTimeThreshold = 3.0f;
    public float stopTime;

    // bounding coefficients
    [Range(0.000001f, 1f)]
    public float alpha = 0.5f;
    [Min(0f)]
    public float beta = 0.75f;
    [Min(1f)]
    public float lambda = 2.3f;
    [Min(0.000001f)]
    public float tau = 0.65f;

    void Start()
    {
        agent = this.GetComponent<NavMeshAgent>();
        agent.stoppingDistance = stoppingDistance;
        animator = this.GetComponentInChildren<Animator>();
        gridComponent = (GridComponent)GameObject.Find("Plane").GetComponent<GridComponent>();
        grid = gridComponent.grid;
        startingPosition = agent.gameObject.transform.position;
        SetAgentDestination();
    }

    public void SetAgentDestination()
    {
        agent.SetDestination(destination);
    }


    void Update() {
        animator.SetFloat("velocity", agent.velocity.magnitude);
        CheckDestinationReached();
        GapSeekingBehaviour();
        FollowingBehaviour();
        StopAndGoBehaviour();
    }

    void CheckDestinationReached()
    {
        if (behaviour == AgentBehaviourType.Default && respawn && agent.remainingDistance <= agent.stoppingDistance)
        {
            this.origin.Respawn(agent.gameObject);
        }
    }

    void StopAndGoBehaviour()
    {
        if (behaviour == AgentBehaviourType.Default &&  Random.Range(1, 1000) == 1) // TODO: Probability.
        {
            behaviour = AgentBehaviourType.StopAndGo;
            agent.isStopped = true;
            stopTime = Time.time + stopTimeThreshold;
        } else if (behaviour == AgentBehaviourType.StopAndGo && stopTime < Time.time)
        {
            agent.isStopped = false;
            behaviour = AgentBehaviourType.Default;
        }
    }

    void GapSeekingBehaviour()
    {
        if (isGapSeeker && behaviour == AgentBehaviourType.Default && Random.value < GetGapSeekingProbability())
        {
            List<Gap> gaps = grid.GapDetection(agent.gameObject.transform.position, gapSearchArea, seeds);
            Gap selectedGap = GapSelection(gaps);

            if (selectedGap != null)
            {
                Gap fullGap = grid.getSearchArea(agent.gameObject.transform.position, gapSearchArea);
                DrawGap(fullGap, Color.blue, 0.5f);

                DrawGap(selectedGap, Color.green);
                GapSeeking(selectedGap);
            }
        } else if (ShouldEndSeeking())
        {
            EndSeeking();
        }
    }

    // Gap selection
    public Gap GapSelection(List<Gap> gaps)
    {
        Vector3 agentPos = agent.gameObject.transform.position;
        Vector3 agentToDestination = destination - agentPos;
        agentToDestination.y = 0;


        // 1. Filter gaps that are in agent's field of vision.
        for (int i = gaps.Count - 1; i >= 0; i--)
        {
            Gap gap = gaps[i];
            Vector3 gapCenter = (grid.GetWorldPosition((int)gap.p1.x, (int)gap.p1.y) + grid.GetWorldPosition((int)gap.p2.x, (int)gap.p2.y)) / 2;
            gap.agentToCenter = gapCenter - agentPos;
            gap.agentToCenter.y = 0;

            if (gap.agentToCenter.magnitude > visionRadius || Vector3.Angle(agent.gameObject.transform.forward, gap.agentToCenter) > (visionAngle / 2))
            {
                gaps.RemoveAt(i);
            }
        }

        // 2. Filter gaps that are too small for our agent.
        for (int i = gaps.Count - 1; i >= 0; i--)
        {
            Gap gap = gaps[i];
            Vector3 p1 = grid.GetWorldPosition((int)gap.p1.x, (int)gap.p1.y);
            Vector3 p2 = grid.GetWorldPosition((int)gap.p2.x, (int)gap.p2.y);
            float gapWidth = Mathf.Abs(p1.x - p2.x);
            float gapHeight = Mathf.Abs(p1.z - p2.z);
            Vector3 agentScale = agent.gameObject.transform.localScale;

            print(agent.radius * Mathf.Max(agentScale.x, agentScale.z));
            if (Mathf.Min(gapWidth, gapHeight) < 2 * (agent.radius * Mathf.Max(agentScale.x, agentScale.z)))
            {
                gaps.RemoveAt(i);
            }
        }

        // 3. Filter gaps would lead agent away from its destination.
        for (int i = gaps.Count - 1; i >= 0; i--)
        {
            if (Vector3.Angle(agentToDestination, gaps[i].agentToCenter) > destinationTresholdAngle)
            {
                gaps.RemoveAt(i);
            }
        }

        // TODO: 4. Filter out gaps that the agent is not closest too, and are searched by other agents.

        // Finally select the gap that has the minimum angle between the gap and the destination.
        if (gaps.Count > 0) {
            Gap selectedGap = gaps[0];
            float minAngle = Vector3.Angle(agentToDestination, selectedGap.agentToCenter);

            foreach (Gap gap in gaps)
            {
                float angle = Vector3.Angle(agentToDestination, gap.agentToCenter);
                if (angle < minAngle)
                {
                    selectedGap = gap;
                    minAngle = angle;
                }
            }
            return selectedGap;
        }

        return null;
    }

    public void GapSeeking(Gap gap)
    {
        behaviour = AgentBehaviourType.GapSeeking;
        // Perform gap seeking.
        // Get limiter objects
        Debug.Log("Get limiter objects");
        List<GameObject> limiterList = grid.DetectLimiters(gap).FindAll(obj => obj != gameObject);

        // Calculate gap speed.
        Debug.Log("Calculate gap speed for limiter count: " + limiterList.Count);
        Vector3 gapSpeed = new Vector3(0, 0, 0);
        if (limiterList.Count > 0)
        {
            foreach (GameObject obj in limiterList)
            {
                gapSpeed += obj.GetComponent<NavMeshAgent>().velocity;
            }
            gapSpeed = gapSpeed / limiterList.Count;
        }
        Debug.Log("Gap speed: " + gapSpeed);

        // Determine seeking time.
        float gapSeekerSpeed = GetGapSeekerSpeed(GetGapArea(gap));
        float agentToGapCenterMagnitude = gap.agentToCenter.magnitude;
        float seekingTime = agentToGapCenterMagnitude / gapSeekerSpeed;

        // Calculate gap position in the future.
        Vector3 gapCenter = (grid.GetWorldPosition((int)gap.p1.x, (int)gap.p1.y) + grid.GetWorldPosition((int)gap.p2.x, (int)gap.p2.y)) / 2;
        Vector3 gapDestination = gapCenter + gapSpeed * seekingTime;

        // Set new agent destination
        seekingStart = Time.time;
        seekingDuration = seekingTime;
        agent.SetDestination(gapDestination);
    }

    private float GetGapArea(Gap gap)
    {
        Vector3 topLeft = grid.GetWorldPosition((int)gap.p1.x, (int)gap.p1.y);
        Vector3 bottomRight = grid.GetWorldPosition((int)gap.p2.x, (int)gap.p2.y);
        float width = Mathf.Abs(bottomRight.x - topLeft.x);
        float height = Mathf.Abs(topLeft.z - bottomRight.z);
        return width * height;
    }

    private float GetGapSeekerSpeed(float gapArea)
    {
        float smin = 4 * Mathf.Pow(agent.radius, 2);
        return this.agent.speed / (1 + Mathf.Exp(-beta * (gapArea - alpha * smin)));
    }

    private bool ShouldEndSeeking()
    {
        if (behaviour == AgentBehaviourType.GapSeeking && ((seekingStart + seekingDuration) < Time.time || agent.remainingDistance <= agent.stoppingDistance))
        {
            return true;
        }
        return false;
    }

    private void EndSeeking()
    {
        agent.SetDestination(destination);
        behaviour = AgentBehaviourType.Default;
    }

    private float GetGapSeekingProbability()
    {
        Vector3 agentToDestination = destination - agent.gameObject.transform.position;
        agentToDestination.y = 0;
        Vector3 startToAgent = agent.gameObject.transform.position - startingPosition;
        startToAgent.y = 0;
        float probability = lambda * agentToDestination.magnitude / startToAgent.magnitude;
        // Debug.Log("Dist to start, end, ratio (end/start): " + startToAgent.magnitude + ", " + agentToDestination.magnitude + ", " + agentToDestination.magnitude / startToAgent.magnitude);
        // Debug.Log("Gap seeking probability: " + Mathf.Clamp(probability, 0, 1));
        return Mathf.Clamp(probability, 0, 1);
    }

    // Following
    public void FollowingBehaviour()
    {
        if (isFollower && behaviour == AgentBehaviourType.Default)
        {
            List<GameObject> candidates = FolloweeDetection();
            GameObject selectedFollowee = FolloweeSelection(candidates);

            Debug.Log("Followee: " + selectedFollowee != null, selectedFollowee);
            if (selectedFollowee != null)
            {
                Debug.Log("Starting following");
                Following(selectedFollowee);
            }
        }
        else if (ShouldEndFollowing())
        {
            EndFollowing();
        }
        else if (behaviour == AgentBehaviourType.Following)
        {
            UpdateFollowingDestionation();
        }

    }
    public List<GameObject> FolloweeDetection() // TODO: test if this works
    {
        // get agent direction and find agents in front
        HashSet<GameObject> candidates = new HashSet<GameObject>();
        Vector3 agentDirection = agent.gameObject.transform.forward;
        Vector3 detectionCenter = agent.gameObject.transform.position + agentDirection * (visionRadius / 2);
        detectionCenter.y = 0.1f;
        Collider[] hitColliders = Physics.OverlapBox(detectionCenter, new Vector3((visionRadius - 0.5f) / 2, 0, (visionRadius - 0.5f) / 2), Quaternion.identity);

        // extract game objects and add to set
        foreach (Collider col in hitColliders)
        {
            candidates.Add(col.gameObject);
        }

        // filter seekers & followers
        List<GameObject> candidateList = candidates.Where(obj => obj.name.Contains("Capsule")).Where(obj => {
            if (obj == gameObject)
            {
                return false;
            }
            if (obj.TryGetComponent(typeof(AgentControl), out Component component))
            {
                if (((AgentControl) component).behaviour == AgentBehaviourType.GapSeeking || ((AgentControl) component).behaviour == AgentBehaviourType.Following)
                {
                    return true;
                }
            }
            return false;
        }).ToList();
        Debug.Log("Followee candidates detected: " + candidateList.Count);
        return candidateList;
    }

    public GameObject FolloweeSelection(List<GameObject> followeeCandidates)
    {
        // check angle
        IEnumerable<GameObject> followeeCandidatesF = followeeCandidates.Where(obj =>
        {
            return Vector3.Angle(agent.gameObject.transform.forward, obj.transform.forward) <= (deviationAngle / 2);
        });
        Debug.Log("Followee candidates after angle: " + followeeCandidatesF.ToList().Count);
        // check if already followed
        followeeCandidatesF = followeeCandidates.Where(obj =>
        {
            if (obj.TryGetComponent(typeof(AgentControl), out Component component))
            {
                if (((AgentControl)component).behaviour == AgentBehaviourType.GapSeeking || ((AgentControl)component).behaviour == AgentBehaviourType.Following)
                {
                    if (((AgentControl)component).followedBy == null)
                    {
                        return true;
                    }
                }
            }
            return false;
        });
        Debug.Log("Followee candidates after already followed: " + followeeCandidatesF.ToList().Count);
        // probability selection
        List<GameObject> filteredList = followeeCandidatesF.ToList();
        if (filteredList.Count == 0)
        {
            return null;
        } else
        {
            float probSum = 0;
            List<float> probs = new List<float>();
            foreach (GameObject obj in filteredList)
            {
                float dist = Vector3.Distance(gameObject.transform.position, obj.transform.position);
                float prob = Mathf.Exp(-tau * dist);
                probs.Add(prob);
                probSum += prob;
            }
            if (probSum == 0)
            {
                return null;
            }

            List<float> reProbs = new List<float>();
            foreach(float val in probs)
            {
                reProbs.Add(val / probSum);
            }

            float selectionProbability = Random.value;
            float accumulatedSum = 0;
            for (int i = 0; i < filteredList.Count; i++)
            {
                accumulatedSum += reProbs[i];
                if (selectionProbability < accumulatedSum)
                {
                    return filteredList[i];
                }
            }
            return null;
        }
    }

    public void Following(GameObject target)
    {   
        if (target == null)
        {
            return;
        }
        float duration = GetFollowingDuration(target);
        Debug.Log("Following duration: " + duration);
        if (duration > 0)
        {
            if (target.TryGetComponent(typeof(AgentControl), out Component component))
            {
                // do stuff
                followingStart = Time.time;
                followingDuration = duration;
                followingTarget = target;
                ((AgentControl)component).followedBy = gameObject;
                behaviour = AgentBehaviourType.Following;
                agent.SetDestination(target.transform.position);
            }
        }
    }

    public float GetFollowingDuration(GameObject target)
    {
        if (target.TryGetComponent(typeof(AgentControl), out Component component))
        {
            float differenceOffset = 0;
            if (((AgentControl)component).behaviour == AgentBehaviourType.GapSeeking)
            {
                return ((AgentControl)component).seekingDuration - (Time.time - ((AgentControl)component).seekingStart);
            }
            else if (((AgentControl)component).behaviour == AgentBehaviourType.Following)
            {
                return ((AgentControl)component).followingDuration - (Time.time - ((AgentControl)component).followingStart);
            }
        }
        return 0;
    }

    public bool ShouldEndFollowing()
    {
        if (behaviour == AgentBehaviourType.Following && (followingStart + followingDuration) < Time.time)
        {
            return true;
        }
        return false;
    }

    public void EndFollowing()
    {
        if (followingTarget != null && followingTarget.TryGetComponent(typeof(AgentControl), out Component component))
        {
            agent.SetDestination(destination);
            behaviour = AgentBehaviourType.Default;
            followingTarget = null;
            ((AgentControl) component).followedBy = null;
        }
    }

    public void UpdateFollowingDestionation()
    {
        if (Vector3.Distance(agent.destination, followingTarget.transform.position) > 0.1f)
        {
            agent.SetDestination(followingTarget.transform.position);
        }
    }


    public void DrawGap(Gap gap, Color color, float duration = 2.5f)
    {
        Vector3 p1 = grid.GetWorldPosition((int)gap.p1.x, (int)gap.p1.y);
        Vector3 p2 = grid.GetWorldPosition((int)gap.p2.x, (int)gap.p2.y);

        Debug.DrawLine(new Vector3(p1.x, 0.1f, p1.z), new Vector3(p2.x, 0.1f, p1.z), color, duration);
        Debug.DrawLine(new Vector3(p2.x, 0.1f, p1.z), new Vector3(p2.x, 0.1f, p2.z), color, duration);
        Debug.DrawLine(new Vector3(p2.x, 0.1f, p2.z), new Vector3(p1.x, 0.1f, p2.z), color, duration);
        Debug.DrawLine(new Vector3(p1.x, 0.1f, p2.z), new Vector3(p1.x, 0.1f, p1.z), color, duration);
    }
}
