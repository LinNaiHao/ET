#ifndef BEHAVIORTREE_NODEFACTORIES_H
#define BEHAVIORTREE_NODEFACTORIES_H

#include <vector>
#include "BehaviorTree/BehaviorNodeIf.h"

namespace Egametang {

class NodeFactories
{
private:
	std::vector<BehaviorNodeFactoryIf*> factories;

public:
	NodeFactories();

	virtual ~NodeFactories();

	virtual BehaviorNodeIf* GetInstance(const BehaviorNodeConf& conf);
};

} // namespace Egametang

#endif // BEHAVIORTREE_NODEFACTORIES_H
