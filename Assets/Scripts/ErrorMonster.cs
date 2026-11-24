using System.Collections.Generic;
using UnityEngine;

/// <summary>«Ошибка»: бродит по лабиринту, замечает игрока, гонится. Каждая ошибка игрока делает её быстрее.</summary>
public class ErrorMonster : MonoBehaviour
{
    public float BaseSpeed = 2.3f, SpeedPerMistake = 0.35f, SightRange = 16f, CatchDistance = 1.1f;
    public int Mistakes;
    public const int MaxSpeedMistakes = 4;
    public float ChaseTimer;
    public bool Chasing => ChaseTimer > 0;

    LevelBuilder _lvl;
    Transform _player;
    FrameAnimator _anim;
    Texture2D[] _idle, _chase;
    List<Vector2Int> _path = new List<Vector2Int>();
    Vector2Int _wanderGoal;
    float _repath;

    public void Init(LevelBuilder lvl, Transform player, FrameAnimator anim, Texture2D[] idle, Texture2D[] chase)
    {
        _lvl = lvl; _player = player; _anim = anim; _idle = idle; _chase = chase;
        _wanderGoal = RandomCell();
    }

    public void ResetTo(Vector2Int cell)
    {
        transform.position = _lvl.CellToWorld(cell);
        ChaseTimer = 0;
        _path.Clear();
        _wanderGoal = RandomCell();
    }

    public void Alert(float seconds) => ChaseTimer = Mathf.Max(ChaseTimer, seconds);

    public void AddMistake()
    {
        Mistakes++;
        Alert(14f);
    }

    /// <summary>Есть ли стена (что угодно, кроме самого игрока) на прямой до игрока, с запасом на ширину «Ошибки».</summary>
    bool WallBetween(Vector3 dir, float dist)
    {
        foreach (var h in Physics.SphereCastAll(transform.position + Vector3.up * 1.0f, 0.3f, dir, dist, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider.transform != _player && !h.collider.transform.IsChildOf(_player)) return true;
        return false;
    }

    Vector2Int RandomCell() => _lvl.FreeCells[Random.Range(0, _lvl.FreeCells.Count)];

    void Update()
    {
        var game = InkGame.I;
        if (game == null || !game.Playing) { _anim.SetFrames(_idle, 4); return; }

        Vector3 to = _player.position - transform.position;
        to.y = 0;
        float dist = to.magnitude;

        // видит игрока, если рядом и между ними нет стены
        if (dist < SightRange && dist > 0.01f &&
            !Physics.Raycast(transform.position + Vector3.up * 1.4f, to / dist, dist - 0.6f, ~0, QueryTriggerInteraction.Ignore))
            Alert(4f);

        ChaseTimer -= Time.deltaTime;
        _anim.SetFrames(Chasing ? _chase : _idle, Chasing ? 8 : 4);

        if (game.Paused) return;
        // ловит, только если между нами нет стены (у угла коридора дистанция мала, но стена мешает)
        if (dist < CatchDistance && !WallBetween(to / Mathf.Max(dist, 1e-3f), dist)) { game.OnCaught(); return; }

        // ускорение от ошибок с потолком: после 4-й «Ошибка» быстрее не становится (иначе от неё не убежать)
        float speed = (BaseSpeed + Mathf.Min(Mistakes, MaxSpeedMistakes) * SpeedPerMistake) * (Chasing ? 1.3f : 0.75f);
        if (game.ProblemOpen) speed *= 0.45f; // пока решаешь пример — подкрадывается медленнее

        // рядом и в той же/соседней клетке — идём прямо к игроку
        var myCell = _lvl.WorldToCell(transform.position);
        var plCell = _lvl.WorldToCell(_player.position);
        // напрямую — только если между нами нет стены (с запасом на ширину «Ошибки»), иначе по проходам
        bool clear = !WallBetween(to / dist, dist);
        if (Chasing && clear && (myCell == plCell || dist < 2.5f))
        {
            transform.position += to.normalized * Mathf.Min(speed * Time.deltaTime, dist);
            return;
        }

        _repath -= Time.deltaTime;
        if (_repath <= 0 || _path.Count == 0)
        {
            _repath = 0.35f;
            var goal = Chasing ? plCell : _wanderGoal;
            if (!Chasing && myCell == _wanderGoal) { _wanderGoal = RandomCell(); goal = _wanderGoal; }
            _path = _lvl.Path(myCell, goal);
        }
        if (_path.Count == 0) { _wanderGoal = RandomCell(); return; }

        Vector3 target = _lvl.CellToWorld(_path[0]);
        Vector3 step = target - transform.position;
        step.y = 0;
        if (step.magnitude < 0.15f) { _path.RemoveAt(0); return; }
        transform.position += step.normalized * Mathf.Min(speed * Time.deltaTime, step.magnitude);
    }
}
